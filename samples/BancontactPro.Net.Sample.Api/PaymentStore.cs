using System.Collections.Concurrent;
using BancontactPro.Helpers;
using BancontactPro.Models;

namespace BancontactPro.Sample.Api;

/// <summary>
/// A payment as this sample tracks it: the Bancontact payment ID, the product it was created for (its credentials are
/// needed to fetch or cancel it later), its latest known status, and the QR link.
/// </summary>
public sealed record PaymentRecord(string PaymentId, BancontactProduct Product, MerchantPaymentStatus Status, long Amount, string? Reference, string? QrCodeUrl, DateTimeOffset UpdatedAt);

/// <summary>Where the sample keeps payments. Swap for a database in a real application.</summary>
public interface IPaymentStore
{
    void Add(PaymentRecord record);

    PaymentRecord? Get(string paymentId);

    /// <summary>Applies a status update and reports whether it changed anything. Duplicate, stale or unknown-payment updates return false.</summary>
    bool TryApplyStatus(string paymentId, MerchantPaymentStatus status, DateTimeOffset observedAt);
}

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly ConcurrentDictionary<string, PaymentRecord> _payments = new();

    public void Add(PaymentRecord record) => _payments[record.PaymentId] = record;

    public PaymentRecord? Get(string paymentId) => _payments.GetValueOrDefault(paymentId);

    public bool TryApplyStatus(string paymentId, MerchantPaymentStatus status, DateTimeOffset observedAt)
    {
        while (_payments.TryGetValue(paymentId, out var current))
        {
            // A terminal status never moves again, and an older observation never overwrites a newer one:
            // callbacks can be retried and can arrive out of order.
            if (IsTerminal(current.Status) || current.Status == status || observedAt < current.UpdatedAt)
            {
                return false;
            }

            if (_payments.TryUpdate(paymentId, current with { Status = status, UpdatedAt = observedAt }, current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTerminal(MerchantPaymentStatus status) =>
        status is MerchantPaymentStatus.SUCCEEDED or MerchantPaymentStatus.FAILED or MerchantPaymentStatus.CANCELLED
            or MerchantPaymentStatus.EXPIRED or MerchantPaymentStatus.AUTHORIZATION_FAILED or MerchantPaymentStatus.VOIDED;
}
