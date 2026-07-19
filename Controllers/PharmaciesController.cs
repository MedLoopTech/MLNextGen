using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PharmaciesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PharmaciesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<Pharmacy>>> GetAll()
    {
        var pharmacies = await _db.Pharmacies
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return Ok(pharmacies);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Pharmacy>> GetById(string id)
    {
        var pharmacy = await _db.Pharmacies.FindAsync(id);
        return pharmacy is null ? NotFound() : Ok(pharmacy);
    }

    public record CreatePharmacyRequest(
        string Name,
        string? Address,
        string? City,
        string? State,
        string? Country,
        string? PhoneNumber);

    [HttpPost]
    public async Task<ActionResult<Pharmacy>> Create([FromBody] CreatePharmacyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var pharmacy = new Pharmacy
        {
            Name = request.Name,
            Address = request.Address,
            City = request.City,
            State = request.State,
            Country = request.Country,
            PhoneNumber = request.PhoneNumber
        };

        _db.Pharmacies.Add(pharmacy);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = pharmacy.Id }, pharmacy);
    }
}
