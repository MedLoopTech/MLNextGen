using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BidsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public BidsController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    // The buyer's own bid history. This works correctly here because
    // BuyerPharmacyId/BuyerUserId are always set server-side at creation
    // (see Create below) — the legacy equivalent screen (OfferStatus/
    // getUserOffers) always returned zero results, because the legacy
    // create-offer endpoint never populated the buyer identity field it
    // filtered on.
    [HttpGet("mine")]
    public async Task<ActionResult<List<Bid>>> GetMine()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var bids = await _db.Bids
            .AsNoTracking()
            .Where(b => b.BuyerPharmacyId == user.PharmacyId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return Ok(bids);
    }

    // The seller's queue: bids on products this pharmacy owns.
    [HttpGet("incoming")]
    public async Task<ActionResult<List<Bid>>> GetIncoming([FromQuery] BidStatus? status)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var query = _db.Bids
            .AsNoTracking()
            .Where(b => b.Product != null && b.Product.PharmacyId == user.PharmacyId);

        if (status is not null)
        {
            query = query.Where(b => b.Status == status);
        }

        var bids = await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
        return Ok(bids);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Bid>> GetById(string id)
    {
        var bid = await _db.Bids.FindAsync(id);
        return bid is null ? NotFound() : Ok(bid);
    }

    // Full negotiation audit trail for a bid — every offer and counter-offer
    // in order. Either party to the bid (or an admin) can view it.
    [HttpGet("{id}/history")]
    public async Task<ActionResult<List<BidNegotiationRound>>> GetHistory(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var bid = await _db.Bids.Include(b => b.Product).FirstOrDefaultAsync(b => b.Id == id);
        if (bid?.Product is null)
        {
            return NotFound();
        }

        var isAdmin = User.IsInRole("Admin");
        var isParty = user?.PharmacyId == bid.BuyerPharmacyId || user?.PharmacyId == bid.Product.PharmacyId;
        if (!isAdmin && !isParty)
        {
            return Forbid();
        }

        var history = await _db.BidNegotiationRounds
            .AsNoTracking()
            .Where(r => r.BidId == id)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        return Ok(history);
    }

    public record CreateBidRequest(string ProductId, int OfferQuantity, double OfferPricePerUnit, string? Message);

    [HttpPost]
    public async Task<ActionResult<Bid>> Create([FromBody] CreateBidRequest request)
    {
        if (request.OfferQuantity <= 0)
        {
            return BadRequest("offerQuantity must be greater than zero.");
        }

        if (request.OfferPricePerUnit <= 0)
        {
            return BadRequest("offerPricePerUnit must be greater than zero.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var product = await _db.Products.FindAsync(request.ProductId);
        if (product is null)
        {
            return NotFound("Product not found.");
        }

        if (product.PharmacyId == user.PharmacyId)
        {
            return BadRequest("You cannot bid on your own pharmacy's listing.");
        }

        if (product.Status != ProductListingStatus.ForRedistribution)
        {
            return BadRequest("This listing is not open for bids.");
        }

        if (product.IsExpired)
        {
            return BadRequest("This listing has expired.");
        }

        // Server-side quantity check — the legacy Offer.cshtml only
        // validated offerQuantity <= availableQty in client-side JS, so a
        // direct API call could bid for more than actually exists.
        if (request.OfferQuantity > product.AvailableQuantity)
        {
            return BadRequest($"Only {product.AvailableQuantity} units are available.");
        }

        // Scoped to THIS buyer's own open bid on THIS product — the legacy
        // check (getOfferNegotiationsByProductId().Any(status == Pending))
        // looked at pending bids from every buyer on the product, so the
        // first pending bid from anyone blocked every other pharmacy from
        // bidding on the same listing at all.
        var hasOpenBid = await _db.Bids.AnyAsync(b =>
            b.ProductId == request.ProductId &&
            b.BuyerPharmacyId == user.PharmacyId &&
            (b.Status == BidStatus.Pending || b.Status == BidStatus.CounteredBySeller || b.Status == BidStatus.CounteredByBuyer));

        if (hasOpenBid)
        {
            return BadRequest("You already have an open bid or negotiation on this product.");
        }

        var bid = new Bid
        {
            ProductId = request.ProductId,
            BuyerPharmacyId = user.PharmacyId,
            BuyerUserId = user.Id,
            OfferQuantity = request.OfferQuantity,
            OfferPricePerUnit = request.OfferPricePerUnit,
            Message = request.Message,
            LastProposedBy = NegotiationParty.Buyer
        };

        _db.Bids.Add(bid);

        _db.BidNegotiationRounds.Add(new BidNegotiationRound
        {
            BidId = bid.Id,
            ProposedBy = NegotiationParty.Buyer,
            ProposedByUserId = user.Id,
            Quantity = bid.OfferQuantity,
            PricePerUnit = bid.OfferPricePerUnit,
            Message = bid.Message
        });

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = bid.Id }, bid);
    }

    // Resolves who the caller is relative to a bid, and whether it's
    // currently their turn to respond (accept / reject / counter).
    private async Task<(ApplicationUser? user, bool isBuyer, bool isSeller, bool isTheirTurn)> ResolvePartyAsync(Bid bid)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null || bid.Product is null)
        {
            return (user, false, false, false);
        }

        var isBuyer = user.PharmacyId == bid.BuyerPharmacyId;
        var isSeller = user.PharmacyId == bid.Product.PharmacyId;

        var sellersTurn = bid.Status is BidStatus.Pending or BidStatus.CounteredByBuyer;
        var buyersTurn = bid.Status is BidStatus.CounteredBySeller;

        var isTheirTurn = (isSeller && sellersTurn) || (isBuyer && buyersTurn);

        return (user, isBuyer, isSeller, isTheirTurn);
    }

    [HttpPut("{id}/accept")]
    public async Task<IActionResult> Accept(string id)
    {
        var bid = await _db.Bids.Include(b => b.Product).FirstOrDefaultAsync(b => b.Id == id);
        if (bid?.Product is null)
        {
            return NotFound();
        }

        var (user, _, _, isTheirTurn) = await ResolvePartyAsync(bid);
        var isAdmin = User.IsInRole("Admin");

        if (bid.Status is not (BidStatus.Pending or BidStatus.CounteredBySeller or BidStatus.CounteredByBuyer))
        {
            return BadRequest("This bid is not open for a response.");
        }

        if (!isAdmin && !isTheirTurn)
        {
            return Forbid();
        }

        // Whoever accepts is agreeing to the CURRENT terms (bid.OfferQuantity/
        // OfferPricePerUnit) — the same values shown to them, never anything
        // supplied in this request, since accepting takes no body at all.
        if (bid.OfferQuantity > bid.Product.AvailableQuantity)
        {
            return BadRequest($"Insufficient stock. Available: {bid.Product.AvailableQuantity}, requested: {bid.OfferQuantity}.");
        }

        bid.Product.LockQuantity += bid.OfferQuantity;
        bid.Status = BidStatus.Approved;
        bid.DecidedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another approval (or listing update) landed first and moved
            // the stock out from under this one.
            return Conflict("Stock changed concurrently — please refresh and try again.");
        }

        return NoContent();
    }

    public record RejectBidRequest(string Reason);

    [HttpPut("{id}/reject")]
    public async Task<IActionResult> Reject(string id, [FromBody] RejectBidRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("A rejection reason is required.");
        }

        var bid = await _db.Bids.Include(b => b.Product).FirstOrDefaultAsync(b => b.Id == id);
        if (bid?.Product is null)
        {
            return NotFound();
        }

        var (_, _, _, isTheirTurn) = await ResolvePartyAsync(bid);
        var isAdmin = User.IsInRole("Admin");

        if (bid.Status is not (BidStatus.Pending or BidStatus.CounteredBySeller or BidStatus.CounteredByBuyer))
        {
            return BadRequest("This bid is not open for a response.");
        }

        if (!isAdmin && !isTheirTurn)
        {
            return Forbid();
        }

        bid.Status = BidStatus.Rejected;
        bid.RejectionReason = request.Reason;
        bid.DecidedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    public record CounterBidRequest(int Quantity, double PricePerUnit, string? Message);

    [HttpPut("{id}/counter")]
    public async Task<ActionResult<Bid>> Counter(string id, [FromBody] CounterBidRequest request)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest("quantity must be greater than zero.");
        }

        if (request.PricePerUnit <= 0)
        {
            return BadRequest("pricePerUnit must be greater than zero.");
        }

        var bid = await _db.Bids.Include(b => b.Product).FirstOrDefaultAsync(b => b.Id == id);
        if (bid?.Product is null)
        {
            return NotFound();
        }

        var (user, isBuyer, isSeller, isTheirTurn) = await ResolvePartyAsync(bid);
        if (user is null)
        {
            return Forbid();
        }

        if (bid.Status is not (BidStatus.Pending or BidStatus.CounteredBySeller or BidStatus.CounteredByBuyer))
        {
            return BadRequest("This bid is not open for a counter-offer.");
        }

        if (!isTheirTurn)
        {
            return Forbid();
        }

        // Nothing is locked until a counter is actually accepted, so this
        // checks against plain available stock, same as bid creation.
        if (request.Quantity > bid.Product.AvailableQuantity)
        {
            return BadRequest($"Only {bid.Product.AvailableQuantity} units are available.");
        }

        var proposingParty = isSeller ? NegotiationParty.Seller : NegotiationParty.Buyer;

        bid.OfferQuantity = request.Quantity;
        bid.OfferPricePerUnit = request.PricePerUnit;
        bid.Message = request.Message;
        bid.LastProposedBy = proposingParty;
        bid.Status = proposingParty == NegotiationParty.Seller ? BidStatus.CounteredBySeller : BidStatus.CounteredByBuyer;

        _db.BidNegotiationRounds.Add(new BidNegotiationRound
        {
            BidId = bid.Id,
            ProposedBy = proposingParty,
            ProposedByUserId = user.Id,
            Quantity = request.Quantity,
            PricePerUnit = request.PricePerUnit,
            Message = request.Message
        });

        await _db.SaveChangesAsync();

        return Ok(bid);
    }

    // Lets the buyer withdraw a negotiation that's still open, in either
    // direction, without needing the seller to reject it first.
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var bid = await _db.Bids.FindAsync(id);

        if (bid is null)
        {
            return NotFound();
        }

        if (user?.PharmacyId != bid.BuyerPharmacyId)
        {
            return Forbid();
        }

        if (bid.Status is not (BidStatus.Pending or BidStatus.CounteredBySeller or BidStatus.CounteredByBuyer))
        {
            return BadRequest("This bid is not open to cancel.");
        }

        bid.Status = BidStatus.Cancelled;
        bid.DecidedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // Paying for an approved bid lives in OrdersController.Checkout, not
    // here — creating an Order is really what "completing payment" means,
    // and that's where the payment gateway call, server-side amount
    // computation, and stock/order atomicity all belong together. See
    // POST /api/orders/checkout.
}
