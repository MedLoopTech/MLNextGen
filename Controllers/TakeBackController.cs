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
public class TakeBackController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILoyaltyService _loyaltyService;
    private readonly INotificationService _notifications;
    private readonly IConfiguration _configuration;

    public TakeBackController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        ILoyaltyService loyaltyService,
        INotificationService notifications,
        IConfiguration configuration)
    {
        _db = db;
        _userManager = userManager;
        _loyaltyService = loyaltyService;
        _notifications = notifications;
        _configuration = configuration;
    }

    // The consumer's own submission history — no PharmacyId required, this
    // is open to any logged-in user, unlike almost every other controller
    // in this app.
    [HttpGet("mine")]
    public async Task<ActionResult<List<TakeBackSubmission>>> GetMine()
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var submissions = await _db.TakeBackSubmissions
            .AsNoTracking()
            .Where(t => t.SubmittedByUserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Ok(submissions);
    }

    // The partner pharmacy's verification queue.
    [HttpGet("incoming")]
    public async Task<ActionResult<List<TakeBackSubmission>>> GetIncoming([FromQuery] TakeBackStatus? status)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PharmacyId is null)
        {
            return Forbid();
        }

        var query = _db.TakeBackSubmissions.AsNoTracking().Where(t => t.PartnerPharmacyId == user.PharmacyId);

        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }

        var submissions = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
        return Ok(submissions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TakeBackSubmission>> GetById(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var submission = await _db.TakeBackSubmissions.FindAsync(id);

        if (submission is null)
        {
            return NotFound();
        }

        var isAdmin = User.IsInRole("Admin");
        var isParty = user?.Id == submission.SubmittedByUserId || user?.PharmacyId == submission.PartnerPharmacyId;
        if (!isAdmin && !isParty)
        {
            return Forbid();
        }

        return Ok(submission);
    }

    public record SubmitTakeBackRequest(string PartnerPharmacyId, string MedicineName, int Quantity, string? Notes, string? PhotoUrl);

    [HttpPost]
    public async Task<ActionResult<TakeBackSubmission>> Submit([FromBody] SubmitTakeBackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MedicineName))
        {
            return BadRequest("Medicine name is required.");
        }

        if (request.Quantity <= 0)
        {
            return BadRequest("Quantity must be greater than zero.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        var pharmacy = await _db.Pharmacies.FindAsync(request.PartnerPharmacyId);
        if (pharmacy is null || !pharmacy.IsActive)
        {
            return BadRequest("Partner pharmacy not found or not active.");
        }

        var submission = new TakeBackSubmission
        {
            SubmittedByUserId = user.Id,
            PartnerPharmacyId = request.PartnerPharmacyId,
            MedicineName = request.MedicineName,
            Quantity = request.Quantity,
            Notes = request.Notes,
            PhotoUrl = request.PhotoUrl
        };

        _db.TakeBackSubmissions.Add(submission);
        await _db.SaveChangesAsync();

        await _notifications.NotifyPharmacyAsync(
            request.PartnerPharmacyId,
            "New take-back submission",
            $"A customer submitted {request.Quantity}x {request.MedicineName} for take-back verification.");

        return CreatedAtAction(nameof(GetById), new { id = submission.Id }, submission);
    }

    // Points are always computed server-side from Quantity times a
    // configured rate (Marketplace:LoyaltyPointsPerTakeBackUnit) — never
    // entered manually by whoever is verifying it. That keeps the reward
    // consistent and not something a staff member (or a staff member
    // colluding with the submitter) can arbitrarily inflate.
    [HttpPut("{id}/verify")]
    public async Task<IActionResult> Verify(string id)
    {
        var user = await _userManager.GetUserAsync(User);
        var submission = await _db.TakeBackSubmissions.FindAsync(id);

        if (submission is null)
        {
            return NotFound();
        }

        if (user?.PharmacyId != submission.PartnerPharmacyId)
        {
            return Forbid();
        }

        if (submission.Status != TakeBackStatus.Submitted)
        {
            return BadRequest("Only submitted take-back requests can be verified.");
        }

        var pointsPerUnit = _configuration.GetValue("Marketplace:LoyaltyPointsPerTakeBackUnit", 10);
        var pointsAwarded = submission.Quantity * pointsPerUnit;

        submission.Status = TakeBackStatus.Verified;
        submission.VerifiedByUserId = user.Id;
        submission.VerifiedAt = DateTime.UtcNow;
        submission.PointsAwarded = pointsAwarded;
        await _db.SaveChangesAsync();

        await _loyaltyService.AwardPointsAsync(
            submission.SubmittedByUserId,
            pointsAwarded,
            LoyaltyPointReason.TakeBackVerified,
            submission.Id);

        await _notifications.NotifyUserAsync(
            submission.SubmittedByUserId,
            "Take-back verified",
            $"Your take-back of {submission.MedicineName} was verified — you earned {pointsAwarded} loyalty points.");

        return NoContent();
    }

    public record RejectTakeBackRequest(string Reason);

    [HttpPut("{id}/reject")]
    public async Task<IActionResult> Reject(string id, [FromBody] RejectTakeBackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("A rejection reason is required.");
        }

        var user = await _userManager.GetUserAsync(User);
        var submission = await _db.TakeBackSubmissions.FindAsync(id);

        if (submission is null)
        {
            return NotFound();
        }

        if (user?.PharmacyId != submission.PartnerPharmacyId)
        {
            return Forbid();
        }

        if (submission.Status != TakeBackStatus.Submitted)
        {
            return BadRequest("Only submitted take-back requests can be rejected.");
        }

        submission.Status = TakeBackStatus.Rejected;
        submission.RejectionReason = request.Reason;
        submission.VerifiedByUserId = user.Id;
        submission.VerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _notifications.NotifyUserAsync(
            submission.SubmittedByUserId,
            "Take-back rejected",
            $"Your take-back of {submission.MedicineName} was rejected: {request.Reason}");

        return NoContent();
    }
}
