namespace MedLoop.NextGen.Models;

public enum BidStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
    PaymentCompleted
}

// Replaces the legacy OfferNegotiationModel/"b2bOffers". The buyer fields
// are always set server-side from the authenticated caller at creation time
// (see BidsController.Create) — the legacy model had a CreatedById field
// that was declared but never actually populated by the controller that
// created offers, which silently broke the buyer's own "My Offers" screen
// and the approve/reject notifications for the entire lifetime of that code.
public class Bid
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProductId { get; set; } = string.Empty;
    public Product? Product { get; set; }

    public string BuyerPharmacyId { get; set; } = string.Empty;
    public Pharmacy? BuyerPharmacy { get; set; }

    public string BuyerUserId { get; set; } = string.Empty;
    public ApplicationUser? BuyerUser { get; set; }

    public int OfferQuantity { get; set; }
    public double OfferPricePerUnit { get; set; }
    public double TotalOfferValue => OfferQuantity * OfferPricePerUnit;

    public string? Message { get; set; }
    public BidStatus Status { get; set; } = BidStatus.Pending;
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}
