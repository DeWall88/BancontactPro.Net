using System.Collections.Concurrent;
using BancontactPro.Models;

namespace BancontactPro.Sample.Api;

/// <summary>A verified callback as received: when it arrived, Bancontact's request ID, and the payload.</summary>
public sealed record ReceivedCallback(DateTimeOffset ReceivedAt, string RequestId, MerchantCallback Payload);

/// <summary>
/// The most recent verified callbacks, kept in memory so they can be inspected through
/// <c>GET /callbacks</c>. A real application would act on each callback instead (update the
/// order, notify the till, ...), and deduplicate on <see cref="ReceivedCallback.RequestId"/>:
/// Bancontact retries callbacks.
/// </summary>
public sealed class CallbackLog
{
    private const int Capacity = 100;
    private readonly ConcurrentQueue<ReceivedCallback> _callbacks = new();

    public void Add(ReceivedCallback callback)
    {
        _callbacks.Enqueue(callback);
        while (_callbacks.Count > Capacity && _callbacks.TryDequeue(out _))
        {
        }
    }

    /// <summary>Newest first, optionally only those for one payment.</summary>
    public IReadOnlyList<ReceivedCallback> Recent(string? paymentId = null) =>
        _callbacks.Reverse().Where(c => paymentId is null || c.Payload.PaymentId == paymentId).ToList();
}
