namespace MedLoop.NextGen.Models;

public enum TakeBackStatus
{
    Submitted,
    Verified,
    Rejected
}

// The core B2C mechanic: a consumer (an ApplicationUser with no
// PharmacyId — not affiliated with any pharmacy) submits unused/excess
// medicine for take-back at a partner pharmacy. Pharmacy staff verify it
// in person and either accept (awarding loyalty points, see
// LoyaltyPointTransaction) or reject it with a reason.
public class TakeBackSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string SubmittedByUserId { get; set; } = string.Empty;
    public ApplicationUser? SubmittedByUser { get; set; }

    public string PartnerPharmacyId { get; set; } = string.Empty;
    public Pharmacy? PartnerPharmacy { get; set; }

    public string MedicineName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }

    public TakeBackStatus Status { get; set; } = TakeBackStatus.Submitted;
    public int? PointsAwarded { get; set; }

    public string? VerifiedByUserId { get; set; }
    public ApplicationUser? VerifiedByUser { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
