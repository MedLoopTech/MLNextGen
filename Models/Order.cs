namespace MedLoop.NextGen.Models;

public enum OrderStatus
{
    Paid,
    Fulfilled,
    Disputed,
    Refunded
}

// Created only by OrdersController.Checkout, once a real (or mock) payment
// gateway charge has actually succeeded — never speculatively, and never
// with a client-supplied amount.
public class Order
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string BidId { get; set; } = string.Empty;
    public Bid? Bid { get; set; }

    public string ProductId { get; set; } = string.Empty;
    public Product? Product { get; set; }

    public string SellerPharmacyId { get; set; } = string.Empty;
    public Pharmacy? SellerPharmacy { get; set; }

    public string BuyerPharmacyId { get; set; } = string.Empty;
    public Pharmacy? BuyerPharmacy { get; set; }

    public int Quantity { get; set; }
    public double UnitPrice { get; set; }
    public double PlatformFeeRate { get; set; }
    public double PlatformFeeAmount { get; set; }
    public double TotalAmount { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Paid;
    public string? PaymentReference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
