namespace MedLoop.NextGen.Models;

// Consolidates the legacy schema's four overlapping status fields
// (status, productStatus, isApproved, isRejected, isDisposed) into one
// lifecycle enum, and replaces the legacy string-typed expiryDate
// (parsed ad hoc with DateTime.TryParse at every call site) with a real
// DateOnly column.
public enum ProductListingStatus
{
    PendingApproval,
    Approved,
    Rejected,
    ForRedistribution,
    Disposed
}

public class Product
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;
    public string? ProductCode { get; set; }
    public string? BatchNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? DosageForm { get; set; }
    public string? PhysicalCondition { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public double? Price { get; set; }
    public double Discount { get; set; }
    public int Quantity { get; set; }
    public int LockQuantity { get; set; }

    public ProductListingStatus Status { get; set; } = ProductListingStatus.PendingApproval;
    public bool Featured { get; set; }

    public string PharmacyId { get; set; } = string.Empty;
    public Pharmacy? Pharmacy { get; set; }

    public string? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string? CategoryId { get; set; }
    public Category? Category { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedById { get; set; }

    public bool IsExpired => ExpiryDate is not null && ExpiryDate.Value < DateOnly.FromDateTime(DateTime.UtcNow);

    public int? DaysUntilExpiry => ExpiryDate is null
        ? null
        : ExpiryDate.Value.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;

    // Available = Quantity minus whatever's currently locked by pending/approved
    // B2B bids. Computed, not stored — avoids the legacy app's pattern of
    // mutating Quantity/LockQuantity directly from multiple unsynchronized
    // call sites.
    public int AvailableQuantity => Math.Max(0, Quantity - LockQuantity);
}
