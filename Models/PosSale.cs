namespace MedLoop.NextGen.Models;

public enum PosPaymentMethod
{
    Cash,
    Card
}

// A single point-of-sale transaction. TotalAmount is always computed
// server-side from each line item's Product.Price at the moment of sale
// (see SalesController.CreateSale) — never accepted from the client, for
// the same reason the B2B checkout amount is never client-supplied.
public class PosSale
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string PharmacyId { get; set; } = string.Empty;
    public Pharmacy? Pharmacy { get; set; }

    public string? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string CashierUserId { get; set; } = string.Empty;
    public ApplicationUser? CashierUser { get; set; }

    public string? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public PosPaymentMethod PaymentMethod { get; set; }
    public double TotalAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PosSaleItem> Items { get; set; } = new List<PosSaleItem>();
}
