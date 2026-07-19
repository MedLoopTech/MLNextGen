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
public class LoyaltyController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILoyaltyService _loyaltyService;

    public LoyaltyController(AppDbContext db, UserManager<ApplicationUser> userManager, ILoyaltyService loyaltyService)
    {
        _db = db;
        _userManager = userManager;
        _loyaltyService = loyaltyService;
    }

    public record BalanceResponse(int Balance, List<LoyaltyPointTransaction> RecentTransactions);

    [HttpGet("balance")]
    public async Task<ActionResult<BalanceResponse>> GetBalance()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var recent = await _db.LoyaltyPointTransactions
            .AsNoTracking()
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .ToListAsync();

        return Ok(new BalanceResponse(user.LoyaltyPoints, recent));
    }

    public record RedeemRequest(int Points);

    // Deducts points and records the redemption — that's the whole scope
    // of this endpoint. It deliberately does NOT try to hand back a
    // discount code, credit, or anything spendable in a purchase flow: this
    // skeleton doesn't yet have a consumer-facing storefront for points to
    // be spent in (checkout and POS are both pharmacy-side flows, not a
    // B2C purchase flow), and guessing at that mechanism without knowing
    // what a consumer is actually meant to buy would just be inventing
    // business logic. Once the B2C storefront exists, this is the natural
    // place to plug in "the redemption produced X" — for now it produces a
    // ledger entry and nothing else, honestly.
    [HttpPost("redeem")]
    public async Task<IActionResult> Redeem([FromBody] RedeemRequest request)
    {
        if (request.Points <= 0)
        {
            return BadRequest("points must be greater than zero.");
        }

        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var success = await _loyaltyService.TryRedeemPointsAsync(userId, request.Points);
        if (!success)
        {
            return BadRequest("Insufficient loyalty points balance.");
        }

        return NoContent();
    }
}
