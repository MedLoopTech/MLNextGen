using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class PromoCodesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PromoCodesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<PromoCode>>> GetAll()
    {
        return Ok(await _db.PromoCodes.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync());
    }

    public record CreatePromoCodeRequest(string Code, double DiscountPercent, DateTime? ExpiresAt, int? MaxRedemptions);

    [HttpPost]
    public async Task<ActionResult<PromoCode>> Create([FromBody] CreatePromoCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest("Code is required.");
        }

        if (request.DiscountPercent is <= 0 or > 100)
        {
            return BadRequest("discountPercent must be between 0 and 100.");
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();

        if (await _db.PromoCodes.AnyAsync(p => p.Code == normalizedCode))
        {
            return Conflict("A promo code with this code already exists.");
        }

        var promoCode = new PromoCode
        {
            Code = normalizedCode,
            DiscountPercent = request.DiscountPercent,
            ExpiresAt = request.ExpiresAt,
            MaxRedemptions = request.MaxRedemptions
        };

        _db.PromoCodes.Add(promoCode);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), promoCode);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var promoCode = await _db.PromoCodes.FindAsync(id);
        if (promoCode is null)
        {
            return NotFound();
        }

        promoCode.IsActive = false;
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
