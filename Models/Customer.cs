namespace MedLoop.NextGen.Models;

// A pharmacy's own walk-in/retail customer, for point-of-sale — distinct
// from Pharmacy (a B2B marketplace participant) and from ApplicationUser
// (someone who logs into the system).
public class Customer
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string PharmacyId { get; set; } = string.Empty;
    public Pharmacy? Pharmacy { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public int LoyaltyPoints { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
