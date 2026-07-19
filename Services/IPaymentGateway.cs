namespace MedLoop.NextGen.Services;

public record PaymentChargeRequest(string BuyerPharmacyId, double Amount, string Currency, string Description);

public record PaymentChargeResult(bool Succeeded, string? Reference, string? FailureReason);

public interface IPaymentGateway
{
    Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken cancellationToken = default);
}
