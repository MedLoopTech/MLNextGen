using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using MedLoop.NextGen.Services;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace MedLoop.NextGen.Jobs;

// Notifies a pharmacy once per run when one of its own "For Redistribution"
// listings is within 30 days of expiry.
//
// Unlike the legacy NearExpiryDealJob/SchedulerManager, every failure path
// here is caught and logged via ILogger — the legacy scheduler code had no
// try/catch around Quartz job bodies at all and logged via
// Console.WriteLine, so a single bad row could silently kill the whole run
// with nothing useful in production logs to diagnose it.
public class NearExpiryNotificationJob : IJob
{
    private const int WarningWindowDays = 30;

    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<NearExpiryNotificationJob> _logger;

    public NearExpiryNotificationJob(AppDbContext db, INotificationService notifications, ILogger<NearExpiryNotificationJob> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("NearExpiryNotificationJob starting.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(WarningWindowDays);

        List<Product> products;
        try
        {
            products = await _db.Products
                .Where(p => p.Status == ProductListingStatus.ForRedistribution)
                .Where(p => p.ExpiryDate != null && p.ExpiryDate >= today && p.ExpiryDate <= cutoff)
                .ToListAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NearExpiryNotificationJob failed to query products — aborting this run.");
            return;
        }

        _logger.LogInformation("NearExpiryNotificationJob found {Count} near-expiry listings.", products.Count);

        foreach (var product in products)
        {
            try
            {
                await _notifications.NotifyPharmacyAsync(
                    product.PharmacyId,
                    "Listing nearing expiry",
                    $"{product.Name} expires on {product.ExpiryDate:yyyy-MM-dd} ({product.DaysUntilExpiry} days) — {product.AvailableQuantity} units still unsold.");
            }
            catch (Exception ex)
            {
                // One bad notification shouldn't abort the rest of the run.
                _logger.LogError(ex, "Failed to notify pharmacy {PharmacyId} about product {ProductId}.", product.PharmacyId, product.Id);
            }
        }

        _logger.LogInformation("NearExpiryNotificationJob finished.");
    }
}
