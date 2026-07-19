using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Services;

// Real, awaited async Task methods. The legacy
// CommonMethods.addPortalNotification/addUserNotification were declared
// `async void` and called without `await` throughout OrderService/
// BidApprovalService — meaning any exception inside them became an
// unobservable exception (able to crash the process depending on host),
// and a request could complete before the notification write actually
// finished, silently dropping notifications under load. Neither is
// possible with this signature: callers must await it, and any failure
// propagates normally to the caller's own try/catch.
public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task NotifyUserAsync(string userId, string title, string body, CancellationToken cancellationToken = default)
    {
        _db.Notifications.Add(new Notification
        {
            RecipientUserId = userId,
            Title = title,
            Body = body
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyPharmacyAsync(string pharmacyId, string title, string body, CancellationToken cancellationToken = default)
    {
        var userIds = await _db.Users
            .Where(u => u.PharmacyId == pharmacyId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            _db.Notifications.Add(new Notification
            {
                RecipientUserId = userId,
                Title = title,
                Body = body
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
