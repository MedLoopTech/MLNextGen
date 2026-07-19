using System.Text.Json.Serialization;

namespace MedLoop.NextGen.Models;

// Append-only audit trail of every offer/counter-offer on a Bid. The Bid
// itself only ever shows the current terms; this is what a dispute
// resolution flow (or just an honest "what actually happened here") would
// query. The legacy app had no equivalent — the closest thing was a
// "CounteredBySupplier" status string in the UI with no backend support
// and no history at all.
public class BidNegotiationRound
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string BidId { get; set; } = string.Empty;

    // EF Core's navigation fixup populates this back-reference to the exact
    // same Bid instance being serialized (Bid.NegotiationRounds -> this ->
    // Bid), which drives System.Text.Json past its 32-level MaxDepth even
    // with ReferenceHandler.IgnoreCycles set globally (confirmed: POST
    // /api/bids hung until this was added). JsonIgnore breaks the cycle at
    // the source instead of relying on depth/reference tracking to catch it.
    [JsonIgnore]
    public Bid? Bid { get; set; }

    public NegotiationParty ProposedBy { get; set; }

    public string ProposedByUserId { get; set; } = string.Empty;
    public ApplicationUser? ProposedByUser { get; set; }

    public int Quantity { get; set; }
    public double PricePerUnit { get; set; }
    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
