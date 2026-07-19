namespace MedLoop.NextGen.Models;

public class Warehouse
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string PharmacyId { get; set; } = string.Empty;
    public Pharmacy? Pharmacy { get; set; }

    // A warehouse is usually tied to one branch, but not always (e.g. a
    // central pharmacy-level warehouse), hence nullable.
    public string? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedById { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
