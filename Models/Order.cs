namespace MedLoop.NextGen.Models;

public enum OrderStatus
{
    Paid,
    Fulfilled,
    Disputed,
    Refunded
}

public enum OrderParty
{
    Buyer,
    Seller
}

public enum DisputeResolution
{
    UpheldOrderStands,   // dispute investigated, order confirmed as fulfilled correctly
    UpheldRefunded       // dispute investigated, buyer was right — refunded
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

    public string? PromoCodeId { get; set; }
    public PromoCode? PromoCode { get; set; }
    public double DiscountAmount { get; set; }

    public double PlatformFeeRate { get; set; }
    public double PlatformFeeAmount { get; set; }
    public double TotalAmount { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Paid;
    public string? PaymentReference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FulfilledAt { get; set; }

    // Dispute fields — replaces the legacy B2BOrderModel's four separate
    // buyer/seller comment+image fields with a single structured record of
    // who raised it, why, and (once resolved) what the outcome was, rather
    // than free-floating text fields with no state machine behind them.
    public OrderParty? DisputeRaisedBy { get; set; }
    public string? DisputeReason { get; set; }
    public DateTime? DisputeRaisedAt { get; set; }

    public DisputeResolution? Resolution { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
