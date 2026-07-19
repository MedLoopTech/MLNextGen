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
public class BranchesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public BranchesController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<List<Branch>>> GetAll([FromQuery] string? pharmacyId)
    {
        var query = _db.Branches.AsNoTracking().Where(b => b.IsActive);

        if (!string.IsNullOrWhiteSpace(pharmacyId))
        {
            query = query.Where(b => b.PharmacyId == pharmacyId);
        }

        return Ok(await query.OrderBy(b => b.Name).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Branch>> GetById(string id)
    {
        var branch = await _db.Branches.FindAsync(id);
        return branch is null ? NotFound() : Ok(branch);
    }

    public record CreateBranchRequest(string Name, string? Address, string? PhoneNumber, double? Latitude, double? Longitude, string? Timing);

    [HttpPost]
    public async Task<ActionResult<Branch>> Create([FromBody] CreateBranchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        // The pharmacy a branch belongs to is derived from the caller's own
        // account, never taken from the request body — this is the ownership
        // check the legacy app's equivalent flows were missing.
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var branch = new Branch
        {
            PharmacyId = user.PharmacyId,
            Name = request.Name,
            Address = request.Address,
            PhoneNumber = request.PhoneNumber,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Timing = request.Timing,
            CreatedById = user.Id
        };

        _db.Branches.Add(branch);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = branch.Id }, branch);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var branch = await _db.Branches.FindAsync(id);

        if (branch is null)
        {
            return NotFound();
        }

        if (user?.PharmacyId != branch.PharmacyId)
        {
            return Forbid();
        }

        branch.IsActive = false;
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
