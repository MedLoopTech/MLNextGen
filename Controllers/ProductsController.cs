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
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProductsController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    // General browse/management view — filtered server-side (unlike the
    // legacy app's getAllProducts, which fetched a page then filtered in
    // memory in C#).
    [HttpGet]
    public async Task<ActionResult<List<Product>>> GetAll(
        [FromQuery] string? pharmacyId,
        [FromQuery] string? categoryId,
        [FromQuery] ProductListingStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(pharmacyId))
        {
            query = query.Where(p => p.PharmacyId == pharmacyId);
        }

        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(items);
    }

    // The MedLoop Connect marketplace view: everything another pharmacy has
    // listed for redistribution that isn't expired. Paged and filtered at
    // the query level, so — unlike the legacy MedLoopConnectController — this
    // never pulls a page of products and then issues one extra query per
    // product just to compute locked/available quantity; AvailableQuantity
    // is a computed property on the entity, not a follow-up round trip.
    [HttpGet("marketplace")]
    public async Task<ActionResult<List<Product>>> GetMarketplaceListings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var items = await _db.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductListingStatus.ForRedistribution)
            .Where(p => p.ExpiryDate == null || p.ExpiryDate >= today)
            .OrderBy(p => p.ExpiryDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Product>> GetById(string id)
    {
        var product = await _db.Products.FindAsync(id);
        return product is null ? NotFound() : Ok(product);
    }

    public record CreateProductRequest(
        string Name,
        string? ProductCode,
        string? BatchNumber,
        string? Manufacturer,
        string? DosageForm,
        DateOnly? ExpiryDate,
        double? Price,
        int Quantity,
        string? BranchId,
        string? WarehouseId,
        string? CategoryId);

    [HttpPost]
    public async Task<ActionResult<Product>> Create([FromBody] CreateProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        if (request.Quantity <= 0)
        {
            return BadRequest("Quantity must be greater than zero.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        // Ownership checks on the optional branch/warehouse — a product
        // can't be filed under another pharmacy's branch or warehouse.
        if (!string.IsNullOrWhiteSpace(request.BranchId))
        {
            var branch = await _db.Branches.FindAsync(request.BranchId);
            if (branch is null || branch.PharmacyId != user.PharmacyId)
            {
                return BadRequest("branchId must belong to your own pharmacy.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.WarehouseId))
        {
            var warehouse = await _db.Warehouses.FindAsync(request.WarehouseId);
            if (warehouse is null || warehouse.PharmacyId != user.PharmacyId)
            {
                return BadRequest("warehouseId must belong to your own pharmacy.");
            }
        }

        var product = new Product
        {
            PharmacyId = user.PharmacyId,
            Name = request.Name,
            ProductCode = request.ProductCode,
            BatchNumber = request.BatchNumber,
            Manufacturer = request.Manufacturer,
            DosageForm = request.DosageForm,
            ExpiryDate = request.ExpiryDate,
            Price = request.Price,
            Quantity = request.Quantity,
            BranchId = request.BranchId,
            WarehouseId = request.WarehouseId,
            CategoryId = request.CategoryId,
            CreatedById = user.Id
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    public record UpdateStatusRequest(ProductListingStatus Status);

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateStatusRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        var product = await _db.Products.FindAsync(id);

        if (product is null)
        {
            return NotFound();
        }

        // Only the owning pharmacy (or an Admin) may change a listing's
        // status — this is exactly the ownership check the legacy
        // BidApproval/OfferStatus controllers were missing.
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && user?.PharmacyId != product.PharmacyId)
        {
            return Forbid();
        }

        product.Status = request.Status;
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
