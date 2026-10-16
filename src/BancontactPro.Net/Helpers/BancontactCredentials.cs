namespace BancontactPro.Helpers;

/// <summary>
/// Everything the library needs to call Bancontact right now: the environment, the API key and PPID
/// per product, and the optional signing key. An <see cref="IBancontactCredentialsProvider"/> returns
/// one on every call, so a change in the host's settings applies to the next call.
/// </summary>
/// <remarks>
/// An empty snapshot (no products, no signing key) is valid: the library then reports every product
/// as not configured when it is used, rather than failing at startup.
/// </remarks>
public sealed record BancontactCredentials
{
    /// <summary>Which Bancontact environment to call. Defaults to <see cref="BancontactEnvironment.Preprod"/>.</summary>
    public BancontactEnvironment Environment { get; init; } = BancontactEnvironment.Preprod;

    /// <summary>
    /// The API key and PPID per product. A product that is missing, or whose pair is incomplete,
    /// is not configured.
    /// </summary>
    public IReadOnlyDictionary<BancontactProduct, BancontactProductCredentials> Products { get; init; } =
        new Dictionary<BancontactProduct, BancontactProductCredentials>();

    /// <summary>The signing key for Refund and Reconciliation requests, or <see langword="null"/> when there is none.</summary>
    public BancontactSigningKey? SigningKey { get; init; }

    /// <summary>
    /// The <c>iss</c> claim of outgoing request signatures. Defaults to <c>"Payconiq"</c>, as in
    /// <see cref="BancontactProOptions.Issuer"/>.
    /// </summary>
    public string Issuer { get; init; } = "Payconiq";

    /// <summary>Returns the complete credential pair for <paramref name="product"/>, or <see langword="null"/> when it isn't configured.</summary>
    public BancontactProductCredentials? GetCredentials(BancontactProduct product) =>
        Products.TryGetValue(product, out var credentials) && credentials.IsComplete ? credentials : null;
}
