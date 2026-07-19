namespace MedLoop.NextGen.Models;

public class Pharmacy
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}
