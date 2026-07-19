namespace MedLoop.NextGen.Models;

// Replaces the legacy app's implied but unbuilt order-feedback concept with
// a real, structured record: one feedback submission per (Order, party),
// enforced at the database level (see AppDbContext), instead of
// free-floating comment fields anyone could set any number of times.
public class OrderFeedback
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string OrderId { get; set; } = string.Empty;
    public Order? Order { get; set; }

    public OrderParty SubmittedBy { get; set; }
    public string SubmittedByPharmacyId { get; set; } = string.Empty;

    public int Rating { get; set; }
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
