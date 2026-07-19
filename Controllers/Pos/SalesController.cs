using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Controllers.Pos;

[ApiController]
[Route("api/pos/sales")]
[Authorize]
public class SalesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public SalesController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<List<PosSale>>> GetAll(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.PosSales.AsNoTracking().Where(s => s.PharmacyId == user.PharmacyId);

        if (from.HasValue)
        {
            query = query.Where(s => s.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(s => s.CreatedAt <= to.Value);
        }

        var sales = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(sales);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PosSale>> GetById(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var sale = await _db.PosSales
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sale is null)
        {
            return NotFound();
        }

        if (user?.PharmacyId != sale.PharmacyId)
        {
            return Forbid();
        }

        return Ok(sale);
    }

    public record SaleLineRequest(string ProductId, int Quantity);
    public record CreateSaleRequest(string? CustomerId, string? BranchId, PosPaymentMethod PaymentMethod, List<SaleLineRequest> Items);

    // Records a completed in-person sale and decrements stock atomically.
    // Like OrdersController.Checkout, the request never carries a price or
    // total — only product IDs and quantities. Every line's price is
    // pulled from Product.Price at the moment of sale, and the total is
    // computed server-side, for the same reason the B2B checkout amount
    // is never client-supplied: a receipt total the client could set
    // itself would just be the same price-tampering bug in a different
    // part of the app.
    [HttpPost]
    public async Task<ActionResult<PosSale>> CreateSale([FromBody] CreateSaleRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest("At least one line item is required.");
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return BadRequest("Every line item quantity must be greater than zero.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(request.CustomerId))
        {
            var customer = await _db.Customers.FindAsync(request.CustomerId);
            if (customer is null || customer.PharmacyId != user.PharmacyId)
            {
                return BadRequest("customerId must belong to your own pharmacy.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.BranchId))
        {
            var branch = await _db.Branches.FindAsync(request.BranchId);
            if (branch is null || branch.PharmacyId != user.PharmacyId)
            {
                return BadRequest("branchId must belong to your own pharmacy.");
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            var sale = new PosSale
            {
                PharmacyId = user.PharmacyId,
                BranchId = request.BranchId,
                CustomerId = request.CustomerId,
                CashierUserId = user.Id,
                PaymentMethod = request.PaymentMethod
            };

            double total = 0;

            foreach (var line in request.Items)
            {
                var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId);
                if (product is null)
                {
                    await transaction.RollbackAsync();
                    return BadRequest($"Product {line.ProductId} not found.");
                }

                if (product.PharmacyId != user.PharmacyId)
                {
                    await transaction.RollbackAsync();
                    return BadRequest($"Product {line.ProductId} does not belong to your pharmacy.");
                }

                if (line.Quantity > product.AvailableQuantity)
                {
                    await transaction.RollbackAsync();
                    return BadRequest($"Only {product.AvailableQuantity} units of {product.Name} are available.");
                }

                var unitPrice = product.Price ?? 0;
                product.Quantity -= line.Quantity;
                total += unitPrice * line.Quantity;

                sale.Items.Add(new PosSaleItem
                {
                    ProductId = product.Id,
                    Quantity = line.Quantity,
                    UnitPriceAtSale = unitPrice
                });
            }

            sale.TotalAmount = total;
            _db.PosSales.Add(sale);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return CreatedAtAction(nameof(GetById), new { id = sale.Id }, sale);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            // xmin concurrency conflict on one of the Products in this sale
            // — e.g. a B2B bid was approved on the same product between the
            // stock check above and the save.
            return Conflict("Stock changed concurrently while processing this sale — please retry.");
        }
    }
}
