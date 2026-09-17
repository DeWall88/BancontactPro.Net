namespace BancontactPro.Signing;

/// <summary>
/// The JOSE header claims required by Bancontact Pro's detached JWS request signing scheme,
/// carried under the <c>https://payconiq.com/</c> namespace and listed in <c>crit</c>.
/// </summary>
/// <param name="Kid">The key id the merchant registered for its hosted JWKS.</param>
/// <param name="MerchantProfileId">The merchant profile id (<c>sub</c> claim).</param>
/// <param name="Issuer">
/// The <c>iss</c> claim value. Payment/Refund specs hardcode this to <c>"Payconiq"</c>; the
/// Reconciliation spec templates it as <c>"{Merchant Id}"</c> instead — genuinely unresolved
/// against the merchant profile id, see docs/research-notes.md. Callers must supply the value
/// appropriate to the API family being called rather than relying on a library-wide default.
/// </param>
/// <param name="IssuedAt">The request timestamp (<c>iat</c> claim), UTC.</param>
/// <param name="RequestId">A unique id for this request (<c>jti</c> claim).</param>
/// <param name="Path">The request path, e.g. <c>/v3/payments/{payment-id}</c> (<c>path</c> claim).</param>
public sealed record JoseHeader(
    string Kid,
    string MerchantProfileId,
    string Issuer,
    DateTimeOffset IssuedAt,
    string RequestId,
    string Path);
