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

        // Scoped to THIS buyer's own pending bid on THIS product — the
        // legacy check (getOfferNegotiationsByProductId().Any(status ==
        // Pending)) looked at pending bids from every buyer on the
        // product, so the first pending bid from anyone blocked every
        // other pharmacy from bidding on the same listing at all.
        var alreadyPending = await _db.Bids.AnyAsync(b =>
            b.ProductId == request.ProductId &&
            b.BuyerPharmacyId == user.PharmacyId &&
            b.Status == BidStatus.Pending);

        if (alreadyPending)
        {
            return BadRequest("You already have a pending bid on this product.");
        }

        var bid = new Bid
        {
            ProductId = request.ProductId,
            BuyerPharmacyId = user.PharmacyId,
            BuyerUserId = user.Id,
            OfferQuantity = request.OfferQuantity,
            OfferPricePerUnit = request.OfferPricePerUnit,
            Message = request.Message
        };

        _db.Bids.Add(bid);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = bid.Id }, bid);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Bid>> GetById(string id)
    {
        var bid = await _db.Bids.FindAsync(id);
        return bid is null ? NotFound() : Ok(bid);
    }

    [HttpPut("{id}/approve")]
    public async Task<IActionResult> Approve(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var bid = await _db.Bids.Include(b => b.Product).FirstOrDefaultAsync(b => b.Id == id);
        if (bid?.Product is null)
        {
            return NotFound();
        }

        // Ownership check — the legacy BidApprovalController.approvenrejectRequest
        // never verified the caller's pharmacy matched the product's
        // seller pharmacy, so anyone (including the buyer themselves)
        // could approve or reject any bid.
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && bid.Product.PharmacyId != user.PharmacyId)
        {
            return Forbid();
        }

        if (bid.Status != BidStatus.Pending)
        {
            return BadRequest("Only pending bids can be approved.");
        }

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
            // the stock out from under this one — the legacy code had no
            // guard against this at all. Ask the caller to re-check and
            // retry rather than silently over-locking stock.
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

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var bid = await _db.Bids.Include(b => b.Product).FirstOrDefaultAsync(b => b.Id == id);
        if (bid?.Product is null)
        {
            return NotFound();
        }

        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && bid.Product.PharmacyId != user.PharmacyId)
        {
            return Forbid();
        }

        if (bid.Status != BidStatus.Pending)
        {
            return BadRequest("Only pending bids can be rejected.");
        }

        bid.Status = BidStatus.Rejected;
        bid.RejectionReason = request.Reason;
        bid.DecidedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // Deliberately NOT implemented yet. The legacy equivalent
    // (OfferStatusController.ProcessBidPayments) hardcoded
    // `bool overallPaymentSuccess = true;` with no actual gateway call —
    // every checkout "succeeded" and marked the order Paid & Closed
    // without ever charging anyone. Rather than port that bug forward,
    // this returns 501 until the real payment gateway integration lands
    // (see the roadmap in README.md).
    [HttpPost("{id}/complete-payment")]
    public async Task<IActionResult> CompletePayment(string id)
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

        if (bid.Status != BidStatus.Approved)
        {
            return BadRequest("Only approved bids can be paid for.");
        }

        return StatusCode(StatusCodes.Status501NotImplemented,
            "Payment gateway integration is not implemented yet in this skeleton. " +
            "This endpoint intentionally does not fake success.");
    }
}
