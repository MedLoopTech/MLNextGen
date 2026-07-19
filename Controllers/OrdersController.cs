using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using MedLoop.NextGen.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IConfiguration _configuration;
    private readonly INotificationService _notifications;

    public OrdersController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IPaymentGateway paymentGateway,
        IConfiguration configuration,
        INotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _paymentGateway = paymentGateway;
        _configuration = configuration;
        _notifications = notifications;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<Order>>> GetMine()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.BuyerPharmacyId == user.PharmacyId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("selling")]
    public async Task<ActionResult<List<Order>>> GetSelling()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.SellerPharmacyId == user.PharmacyId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetById(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var order = await _db.Orders.FindAsync(id);

        if (order is null)
        {
            return NotFound();
        }

        var isAdmin = User.IsInRole("Admin");
        var isParty = user?.PharmacyId == order.BuyerPharmacyId || user?.PharmacyId == order.SellerPharmacyId;
        if (!isAdmin && !isParty)
        {
            return Forbid();
        }

        return Ok(order);
    }

    public record CheckoutRequest(string BidId);

    // Charges the buyer for an approved bid and creates the resulting
    // order, atomically. The amount charged is ALWAYS recomputed here from
    // the bid's own stored quantity/price plus the server-configured
    // platform fee rate — the request body only ever carries a bid ID.
    //
    // This is the direct fix for the legacy app's most severe payment bug:
    // PaymentController.ProcessCheckout took `amount` as a plain query
    // string parameter and passed it straight to the gateway with no
    // server-side recomputation, so any buyer could edit the URL and pay
    // an arbitrary amount — e.g. $0.01 — for a real order.
    //
    // Known limitation: if ChargeAsync succeeds but the transaction below
    // fails to commit (e.g. a DB outage between the two), the buyer has
    // been charged with no order recorded. A real gateway integration
    // needs an idempotency key on the charge plus a reconciliation job to
    // catch this; that's out of scope for this skeleton and MUST be
    // addressed before this is used with a real (non-mock) payment
    // gateway.
    [HttpPost("checkout")]
    public async Task<ActionResult<Order>> Checkout([FromBody] CheckoutRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var bid = await _db.Bids.Include(b => b.Product).FirstOrDefaultAsync(b => b.Id == request.BidId);
        if (bid?.Product is null)
        {
            return NotFound("Bid not found.");
        }

        if (bid.BuyerPharmacyId != user.PharmacyId)
        {
            return Forbid();
        }

        if (bid.Status != BidStatus.Approved)
        {
            return BadRequest("Only approved bids can be paid for.");
        }

        if (await _db.Orders.AnyAsync(o => o.BidId == bid.Id))
        {
            return Conflict("This bid has already been paid for.");
        }

        var platformFeeRate = _configuration.GetValue("Marketplace:PlatformFeeRate", 0.0);
        var subtotal = bid.TotalOfferValue;
        var platformFeeAmount = subtotal * platformFeeRate;
        var totalAmount = subtotal + platformFeeAmount;

        var chargeResult = await _paymentGateway.ChargeAsync(new PaymentChargeRequest(
            user.PharmacyId,
            totalAmount,
            "USD",
            $"MedLoop order for bid {bid.Id}"));

        if (!chargeResult.Succeeded)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, chargeResult.FailureReason ?? "Payment failed.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // Re-fetch for a fresh xmin concurrency token before mutating.
            var product = await _db.Products.FirstAsync(p => p.Id == bid.ProductId);

            if (product.LockQuantity < bid.OfferQuantity || product.Quantity < bid.OfferQuantity)
            {
                await transaction.RollbackAsync();
                // Note: the gateway charge already succeeded at this point.
                // A real integration needs to void/refund it here — the
                // mock gateway has no such API since nothing was actually
                // charged, so this is left as a TODO for the real
                // integration.
                return Conflict("Product stock changed since this bid was approved. Contact the seller.");
            }

            product.Quantity -= bid.OfferQuantity;
            product.LockQuantity -= bid.OfferQuantity;

            var order = new Order
            {
                BidId = bid.Id,
                ProductId = product.Id,
                SellerPharmacyId = product.PharmacyId,
                BuyerPharmacyId = bid.BuyerPharmacyId,
                Quantity = bid.OfferQuantity,
                UnitPrice = bid.OfferPricePerUnit,
                PlatformFeeRate = platformFeeRate,
                PlatformFeeAmount = platformFeeAmount,
                TotalAmount = totalAmount,
                Status = OrderStatus.Paid,
                PaymentReference = chargeResult.Reference
            };

            _db.Orders.Add(order);
            bid.Status = BidStatus.PaymentCompleted;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _notifications.NotifyPharmacyAsync(
                order.SellerPharmacyId,
                "Order paid",
                $"An order for {product.Name} ({order.Quantity} units, {order.TotalAmount:C}) has been paid. Please prepare it for fulfillment.");

            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            // Covers both the xmin concurrency conflict on Product and the
            // unique-index conflict on Order.BidId (a duplicate/racing
            // checkout for the same bid).
            return Conflict("This order could not be completed due to a concurrent update — please retry.");
        }
    }

    // Seller confirms they've shipped/handed over the goods. Only the
    // selling pharmacy can do this, and only from Paid — replaces the
    // legacy B2BOrderModel.FulfillmentStatus string, which any code path
    // could set to anything with no ownership check and no state machine.
    [HttpPut("{id}/fulfill")]
    public async Task<IActionResult> Fulfill(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var order = await _db.Orders.FindAsync(id);

        if (order is null)
        {
            return NotFound();
        }

        if (user?.PharmacyId != order.SellerPharmacyId)
        {
            return Forbid();
        }

        if (order.Status != OrderStatus.Paid)
        {
            return BadRequest("Only paid orders can be marked fulfilled.");
        }

        order.Status = OrderStatus.Fulfilled;
        order.FulfilledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _notifications.NotifyPharmacyAsync(
            order.BuyerPharmacyId,
            "Order fulfilled",
            "Your order has been marked as shipped/fulfilled by the seller.");

        return NoContent();
    }

    public record RaiseDisputeRequest(string Reason);

    // Either party can raise a dispute on a paid or fulfilled order — e.g.
    // wrong quantity received, damaged goods, seller never shipped. This
    // replaces the legacy app's free-floating BuyerComments/
    // SellerDisputeComment fields (which anyone could set, with no actual
    // "this order is now disputed" state) with a real status transition.
    [HttpPut("{id}/dispute")]
    public async Task<IActionResult> RaiseDispute(string id, [FromBody] RaiseDisputeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("A dispute reason is required.");
        }

        var user = await _userManager.GetUserAsync(User);
        var order = await _db.Orders.FindAsync(id);

        if (order is null)
        {
            return NotFound();
        }

        OrderParty party;
        if (user?.PharmacyId == order.BuyerPharmacyId)
        {
            party = OrderParty.Buyer;
        }
        else if (user?.PharmacyId == order.SellerPharmacyId)
        {
            party = OrderParty.Seller;
        }
        else
        {
            return Forbid();
        }

        if (order.Status is not (OrderStatus.Paid or OrderStatus.Fulfilled))
        {
            return BadRequest("Only paid or fulfilled orders can be disputed.");
        }

        order.Status = OrderStatus.Disputed;
        order.DisputeRaisedBy = party;
        order.DisputeReason = request.Reason;
        order.DisputeRaisedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var notifyPharmacyId = party == OrderParty.Buyer ? order.SellerPharmacyId : order.BuyerPharmacyId;
        await _notifications.NotifyPharmacyAsync(
            notifyPharmacyId,
            "Order disputed",
            $"A dispute was raised on your order: {request.Reason}");

        return NoContent();
    }

    public record ResolveDisputeRequest(DisputeResolution Resolution, string Notes);

    // Admin-only: closes out a dispute one way or the other. Deliberately
    // not left to either party to self-resolve — that's what makes it a
    // dispute rather than a negotiation.
    [HttpPut("{id}/resolve-dispute")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResolveDispute(string id, [FromBody] ResolveDisputeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            return BadRequest("Resolution notes are required.");
        }

        var order = await _db.Orders.FindAsync(id);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != OrderStatus.Disputed)
        {
            return BadRequest("Only disputed orders can be resolved.");
        }

        order.Resolution = request.Resolution;
        order.ResolutionNotes = request.Notes;
        order.ResolvedAt = DateTime.UtcNow;
        order.Status = request.Resolution == DisputeResolution.UpheldRefunded
            ? OrderStatus.Refunded
            : OrderStatus.Fulfilled;

        // NOTE: this only changes the order's status — it does not call any
        // payment gateway refund/void API. IPaymentGateway currently has no
        // RefundAsync method because MockPaymentGateway never actually
        // charged anything. Before this goes live with a real gateway, a
        // refund must be wired in here (or in a separate step this action
        // triggers), or "Refunded" orders will silently not actually be
        // refunded — the same category of bug as the legacy app's payment
        // stub, just moved to a different endpoint.
        await _db.SaveChangesAsync();

        var resolutionSummary = $"Dispute resolved: {request.Resolution} — {request.Notes}";
        await _notifications.NotifyPharmacyAsync(order.BuyerPharmacyId, "Dispute resolved", resolutionSummary);
        await _notifications.NotifyPharmacyAsync(order.SellerPharmacyId, "Dispute resolved", resolutionSummary);

        return NoContent();
    }
}
