namespace MedLoop.NextGen.Models;

// Applied at B2B checkout (OrdersController.Checkout) as a percentage
// discount on the order subtotal, before the platform fee is calculated.
public class PromoCode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Stored and compared uppercased so lookups are case-insensitive
    // without relying on a case-insensitive collation.
    public string Code { get; set; } = string.Empty;

    public double DiscountPercent { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxRedemptions { get; set; }
    public int TimesRedeemed { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsValidNow =>
        IsActive &&
        (ExpiresAt is null || ExpiresAt >= DateTime.UtcNow) &&
        (MaxRedemptions is null || TimesRedeemed < MaxRedemptions);
}
