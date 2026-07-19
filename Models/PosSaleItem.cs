namespace MedLoop.NextGen.Models;

public class PosSaleItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string PosSaleId { get; set; } = string.Empty;
    public PosSale? PosSale { get; set; }

    public string ProductId { get; set; } = string.Empty;
    public Product? Product { get; set; }

    public int Quantity { get; set; }

    // Copied from Product.Price at the moment of sale — a later price
    // change on the product must never retroactively change a past
    // receipt's numbers.
    public double UnitPriceAtSale { get; set; }

    public double Subtotal => Quantity * UnitPriceAtSale;
}
