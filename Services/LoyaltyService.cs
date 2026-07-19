using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Services;

public class LoyaltyService : ILoyaltyService
{
    private readonly AppDbContext _db;

    public LoyaltyService(AppDbContext db)
    {
        _db = db;
    }

    public async Task AwardPointsAsync(string userId, int points, LoyaltyPointReason reason, string? relatedTakeBackId = null, CancellationToken cancellationToken = default)
    {
        if (points <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Points to award must be positive.");
        }

        var user = await _db.Users.FirstAsync(u => u.Id == userId, cancellationToken);
        user.LoyaltyPoints += points;

        _db.Add(new LoyaltyPointTransaction
        {
            UserId = userId,
            PointsDelta = points,
            Reason = reason,
            RelatedTakeBackId = relatedTakeBackId
        });

        // ApplicationUser uses an xmin concurrency token (see AppDbContext),
        // so a concurrent balance change on the same user throws here
        // rather than silently overwriting one side's update.
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryRedeemPointsAsync(string userId, int points, CancellationToken cancellationToken = default)
    {
        if (points <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Points to redeem must be positive.");
        }

        var user = await _db.Users.FirstAsync(u => u.Id == userId, cancellationToken);
        if (user.LoyaltyPoints < points)
        {
            return false;
        }

        user.LoyaltyPoints -= points;

        _db.Add(new LoyaltyPointTransaction
        {
            UserId = userId,
            PointsDelta = -points,
            Reason = LoyaltyPointReason.Redeemed
        });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
