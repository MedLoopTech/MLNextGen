namespace MedLoop.NextGen.Models;

public enum LoyaltyPointReason
{
    TakeBackVerified,
    Redeemed,
    AdminAdjustment
}

// Append-only ledger — the real source of truth for how a user's
// ApplicationUser.LoyaltyPoints balance got to whatever it currently is.
// Same audit-trail discipline already applied elsewhere in this codebase
// (BidNegotiationRound for bids, structured dispute fields for orders):
// a running balance with no history is exactly the kind of thing that's
// impossible to reconcile or dispute later.
public class LoyaltyPointTransaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    // Positive = earned, negative = redeemed/spent.
    public int PointsDelta { get; set; }
    public LoyaltyPointReason Reason { get; set; }
    public string? RelatedTakeBackId { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
