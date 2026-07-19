namespace MedLoop.NextGen.Services;

// Development/testing only — always "succeeds" and returns a fake
// reference. Registered in Program.cs only until a real gateway
// (Mastercard/MCB, matching the legacy app's PaymentApi config) is wired
// up. The important difference from the legacy app: this is an explicit,
// clearly-named, swappable IPaymentGateway implementation. The legacy
// OfferStatusController hardcoded `bool overallPaymentSuccess = true;`
// directly inside the checkout method itself, with no gateway abstraction
// and nothing marking it as a stand-in — replacing THIS with a real
// gateway is a one-line DI change in Program.cs, not a rewrite of the
// checkout flow, and it's obvious from the class name that no real charge
// happens here.
public class MockPaymentGateway : IPaymentGateway
{
    public Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken cancellationToken = default)
    {
        var reference = $"MOCK-{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentChargeResult(true, reference, null));
    }
}
