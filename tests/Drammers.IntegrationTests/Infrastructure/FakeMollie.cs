using System.Collections.Concurrent;
using Drammers.Infrastructure.Payments;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>Mollie in het geheugen: betalingen beginnen als <c>open</c>; de test zet ze op <c>paid</c> of <c>failed</c>.</summary>
public sealed class FakeMollie : IMollieClient
{
    private int _next;

    public ConcurrentDictionary<string, MolliePayment> Payments { get; } = new();

    public ConcurrentBag<MollieNewPayment> Created { get; } = [];

    public Task<MolliePayment> CreatePaymentAsync(MollieNewPayment payment, CancellationToken cancellationToken)
    {
        Created.Add(payment);
        var id = $"tr_test{Interlocked.Increment(ref _next)}";
        var created = new MolliePayment(id, "open", $"https://mollie.test/checkout/{id}", payment.OrderId, payment.AmountCents, null);
        Payments[id] = created;
        return Task.FromResult(created);
    }

    public Task<MolliePayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken) =>
        Payments.TryGetValue(paymentId, out var p) ? Task.FromResult(p) : throw new MollieException("Onbekende betaling");

    public void SetStatus(string paymentId, string status) =>
        Payments[paymentId] = Payments[paymentId] with { Status = status, PaidAt = status == "paid" ? DateTime.UtcNow : null };
}
