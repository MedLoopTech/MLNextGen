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
    private readonly IEmailSender _emailSender;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(AppDbContext db, IEmailSender emailSender, ILogger<NotificationService> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _logger = logger;
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

        var email = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);

        await TrySendEmailAsync(email, title, body, cancellationToken);
    }

    public async Task NotifyPharmacyAsync(string pharmacyId, string title, string body, CancellationToken cancellationToken = default)
    {
        var recipients = await _db.Users
            .Where(u => u.PharmacyId == pharmacyId)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            _db.Notifications.Add(new Notification
            {
                RecipientUserId = recipient.Id,
                Title = title,
                Body = body
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            await TrySendEmailAsync(recipient.Email, title, body, cancellationToken);
        }
    }

    // Email is a best-effort secondary channel — the in-app Notification
    // row above is the source of truth and is always created regardless.
    // A transient email failure (e.g. Gmail API not yet configured in a
    // given environment) is logged and swallowed here rather than failing
    // the whole notification call. This is a narrow, deliberate exception
    // for a genuinely best-effort side channel — not the kind of blanket
    // "catch everything and hide it" pattern the legacy app's audit
    // flagged throughout its controllers, where real errors were caught
    // and only ever surfaced as an unhelpful message to the end user.
    private async Task TrySendEmailAsync(string? emailAddress, string title, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
        {
            return;
        }

        try
        {
            await _emailSender.SendAsync(emailAddress, title, body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification email to {Email}", emailAddress);
        }
    }
}
