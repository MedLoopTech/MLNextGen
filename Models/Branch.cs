namespace MedLoop.NextGen.Models;

public class Branch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string PharmacyId { get; set; } = string.Empty;
    public Pharmacy? Pharmacy { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? PhoneNumber { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Timing { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedById { get; set; }

    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
