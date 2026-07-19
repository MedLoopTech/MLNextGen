using MedLoop.NextGen.Models;

namespace MedLoop.NextGen.Services;

public interface ILoyaltyService
{
    Task AwardPointsAsync(string userId, int points, LoyaltyPointReason reason, string? relatedTakeBackId = null, CancellationToken cancellationToken = default);

    // Returns false (without throwing) if the user doesn't have enough
    // points — insufficient balance is an expected outcome here, not an
    // exceptional one.
    Task<bool> TryRedeemPointsAsync(string userId, int points, CancellationToken cancellationToken = default);
}
