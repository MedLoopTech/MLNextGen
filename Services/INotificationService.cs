namespace MedLoop.NextGen.Services;

public interface INotificationService
{
    Task NotifyUserAsync(string userId, string title, string body, CancellationToken cancellationToken = default);

    // Notifies every user belonging to a pharmacy — mirrors the legacy
    // behavior of fanning a notification out to all of a pharmacy's portal
    // users, just with a real awaited implementation behind it.
    Task NotifyPharmacyAsync(string pharmacyId, string title, string body, CancellationToken cancellationToken = default);
}
