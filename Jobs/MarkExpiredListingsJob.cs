using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using MedLoop.NextGen.Services;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace MedLoop.NextGen.Jobs;

// Marks listings past their expiry date as Disposed and notifies the
// owning pharmacy. Same error-handling discipline as
// NearExpiryNotificationJob: every step is caught and logged, nothing
// crashes the scheduler thread silently, and one bad row doesn't stop the
// rest of the run.
public class MarkExpiredListingsJob : IJob
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<MarkExpiredListingsJob> _logger;

    public MarkExpiredListingsJob(AppDbContext db, INotificationService notifications, ILogger<MarkExpiredListingsJob> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("MarkExpiredListingsJob starting.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        List<Product> expiredProducts;
        try
        {
            expiredProducts = await _db.Products
                .Where(p => p.Status != ProductListingStatus.Disposed)
                .Where(p => p.ExpiryDate != null && p.ExpiryDate < today)
                .ToListAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkExpiredListingsJob failed to query products — aborting this run.");
            return;
        }

        _logger.LogInformation("MarkExpiredListingsJob found {Count} expired listings to mark.", expiredProducts.Count);

        foreach (var product in expiredProducts)
        {
            try
            {
                product.Status = ProductListingStatus.Disposed;
                await _db.SaveChangesAsync(context.CancellationToken);

                await _notifications.NotifyPharmacyAsync(
                    product.PharmacyId,
                    "Listing marked as disposed",
                    $"{product.Name} passed its expiry date ({product.ExpiryDate:yyyy-MM-dd}) and has been automatically marked as disposed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark product {ProductId} as disposed.", product.Id);

                // Defensive: don't let a failed save on this product leave a
                // stale tracked entry that could interfere with saving the
                // next one in this loop.
                _db.Entry(product).State = EntityState.Detached;
            }
        }

        _logger.LogInformation("MarkExpiredListingsJob finished.");
    }
}
