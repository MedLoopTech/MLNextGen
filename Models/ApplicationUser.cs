using Microsoft.AspNetCore.Identity;

namespace MedLoop.NextGen.Models;

// Extends the built-in Identity user with the domain fields this app
// actually needs. One account type covers two very different roles:
//   - Pharmacy staff: PharmacyId is set, they manage that pharmacy's
//     branches/products/bids/POS sales.
//   - Consumers (the B2C "dawadaira" take-back program): PharmacyId is
//     null — they're not affiliated with any pharmacy, they submit medicine
//     take-back requests to partner pharmacies and earn LoyaltyPoints.
public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public string? PharmacyId { get; set; }
    public Pharmacy? Pharmacy { get; set; }

    // Materialized running balance for fast reads. The source of truth /
    // audit trail is LoyaltyPointTransaction — this field should only ever
    // be changed by ILoyaltyService, in lockstep with a ledger row, never
    // updated directly.
    public int LoyaltyPoints { get; set; }
}
