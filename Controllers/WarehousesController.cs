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
public class WarehousesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public WarehousesController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<List<Warehouse>>> GetAll([FromQuery] string? pharmacyId, [FromQuery] string? branchId)
    {
        var query = _db.Warehouses.AsNoTracking().Where(w => w.IsActive);

        if (!string.IsNullOrWhiteSpace(pharmacyId))
        {
            query = query.Where(w => w.PharmacyId == pharmacyId);
        }

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(w => w.BranchId == branchId);
        }

        return Ok(await query.OrderBy(w => w.Name).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Warehouse>> GetById(string id)
    {
        var warehouse = await _db.Warehouses.FindAsync(id);
        return warehouse is null ? NotFound() : Ok(warehouse);
    }

    public record CreateWarehouseRequest(string Name, string? Address, string? PhoneNumber, string? BranchId);

    [HttpPost]
    public async Task<ActionResult<Warehouse>> Create([FromBody] CreateWarehouseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(request.BranchId))
        {
            var branch = await _db.Branches.FindAsync(request.BranchId);
            if (branch is null || branch.PharmacyId != user.PharmacyId)
            {
                return BadRequest("branchId must belong to your own pharmacy.");
            }
        }

        var warehouse = new Warehouse
        {
            PharmacyId = user.PharmacyId,
            BranchId = request.BranchId,
            Name = request.Name,
            Address = request.Address,
            PhoneNumber = request.PhoneNumber,
            CreatedById = user.Id
        };

        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = warehouse.Id }, warehouse);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var warehouse = await _db.Warehouses.FindAsync(id);

        if (warehouse is null)
        {
            return NotFound();
        }

        if (user?.PharmacyId != warehouse.PharmacyId)
        {
            return Forbid();
        }

        warehouse.IsActive = false;
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
