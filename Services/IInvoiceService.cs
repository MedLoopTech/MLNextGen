using MedLoop.NextGen.Models;

namespace MedLoop.NextGen.Services;

public interface IInvoiceService
{
    // Caller is responsible for loading Order.Product/SellerPharmacy/
    // BuyerPharmacy (via .Include()) first — this only renders what's
    // already populated on the entity, it doesn't query the database
    // itself.
    byte[] GenerateOrderInvoice(Order order);
}
