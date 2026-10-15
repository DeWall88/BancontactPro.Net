using BancontactPro.Models;

namespace BancontactPro.Payments;

/// <summary>The Bancontact Pro Payment API (v3) — online, VOID-mode, and static QR payments.</summary>
public interface IPaymentClient
{
    /// <summary>Creates an online payment (<c>POST v3/payments</c>).</summary>
    Task<CreatePaymentResponse> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets a payment by id (<c>GET v3/payments/{id}</c>) — the documented polling fallback.</summary>
    Task<Payment> GetAsync(string paymentId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a payment while it's still <c>PENDING</c>/<c>IDENTIFIED</c> (<c>DELETE v3/payments/{id}</c>).</summary>
    Task CancelAsync(string paymentId, CancellationToken cancellationToken = default);

    /// <summary>Searches payments by filter, with paging (<c>POST v3/payments/search</c>).</summary>
    /// <param name="query">Filter criteria. All fields are optional.</param>
    /// <param name="page">Zero-based page index. Defaults to 0 server-side.</param>
    /// <param name="size">Page size, max 100. Defaults to 10 server-side.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<PaymentSearchResponse> SearchAsync(PaymentSearchQuery query, int? page = null, int? size = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges a payment status callback for the VOID-mode flow
    /// (<see cref="MerchantPaymentStatus.PENDING_MERCHANT_ACKNOWLEDGEMENT"/>) (<c>POST v3/payments/{id}/acknowledge</c>).
    /// </summary>
    Task AcknowledgeAsync(string paymentId, PaymentAcknowledgeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the debtor's unmasked IBAN for issuing a refund transfer directly
    /// (<c>GET v3/payments/{id}/debtor/refundIban</c>). Requires the payment to be <c>SUCCEEDED</c>.
    /// </summary>
    Task<DebtorRefundIban> GetDebtorRefundIbanAsync(string paymentId, CancellationToken cancellationToken = default);

    /// <summary>Creates a static QR (in-person/POS) payment (<c>POST v3/payments/pos</c>).</summary>
    Task<CreatePaymentResponse> CreateStaticQrPaymentAsync(CreateStaticQrPaymentRequest request, CancellationToken cancellationToken = default);
}
