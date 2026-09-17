using BancontactPro.Models;

namespace BancontactPro.Webhooks;

/// <summary>
/// A callback that passed signature verification, along with its unique request id
/// (<c>jti</c>) for the consuming application's own idempotency/dedupe tracking — Bancontact
/// retries callbacks for up to 24h on non-200 responses, so duplicate deliveries for the same
/// payment/status are expected, not a bug to work around here.
/// </summary>
public sealed record VerifiedCallback(MerchantCallback Payload, string RequestId);
