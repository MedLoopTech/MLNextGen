namespace MedLoop.NextGen.Models;

public enum BidStatus
{
    Pending,           // awaiting the seller's response to the buyer's offer
    CounteredBySeller, // awaiting the buyer's response to the seller's counter
    CounteredByBuyer,  // awaiting the seller's response to the buyer's counter
    Approved,          // current terms accepted by both sides, ready for checkout
    Rejected,
    Cancelled,         // withdrawn by the buyer before either side accepted
    PaymentCompleted
}

public enum NegotiationParty
{
    Buyer,
    Seller
}

// Replaces the legacy OfferNegotiationModel/"b2bOffers". The buyer fields
// are always set server-side from the authenticated caller at creation time
// (see BidsController.Create) — the legacy model had a CreatedById field
// that was declared but never actually populated by the controller that
// created offers, which silently broke the buyer's own "My Offers" screen
// and the approve/reject notifications for the entire lifetime of that code.
//
// OfferQuantity/OfferPricePerUnit always reflect the CURRENT terms on the
// table — whichever side proposed most recently (see BidsController.Counter).
// The full negotiation history, including every prior round, lives in
// BidNegotiationRound, not on this record.
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

    // Which side proposed the terms currently on the table — determines
    // whose turn it is to accept/reject/counter next.
    public NegotiationParty LastProposedBy { get; set; } = NegotiationParty.Buyer;

    public string? Message { get; set; }
    public BidStatus Status { get; set; } = BidStatus.Pending;
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }

    public ICollection<BidNegotiationRound> NegotiationRounds { get; set; } = new List<BidNegotiationRound>();
}
