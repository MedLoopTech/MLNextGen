using Microsoft.AspNetCore.Identity;

namespace MedLoop.NextGen.Models;

// Extends the built-in Identity user with the domain fields this app actually
// needs (mirrors the legacy PortalUser/User shape, minus the fields that don't
// belong on an identity record).
public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public string? PharmacyId { get; set; }
    public Pharmacy? Pharmacy { get; set; }
}
