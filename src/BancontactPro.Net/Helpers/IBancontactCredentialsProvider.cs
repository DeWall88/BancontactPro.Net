namespace BancontactPro.Helpers;

/// <summary>
/// Supplies the credentials the library uses, on every call. Implement it when the host keeps its
/// credentials somewhere that can change at runtime (a database, a settings page, a secret store) and
/// register it with <c>AddBancontactPro&lt;TProvider&gt;()</c>. Hosts with static settings can use
/// <c>AddBancontactProIntegration(IConfiguration)</c>, which registers <see cref="ConfigurationCredentialsProvider"/>.
/// </summary>
/// <remarks>
/// The library asks for the credentials each time it creates a client or looks up a callback signing
/// key, and keeps nothing between those calls, so a change applies to the next call. It does not cache
/// the result: a provider that is expensive to read should cache briefly itself. Returning an empty
/// <see cref="BancontactCredentials"/> is how a provider says "not configured".
/// </remarks>
public interface IBancontactCredentialsProvider
{
    /// <summary>Returns the current credentials.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<BancontactCredentials> GetCredentialsAsync(CancellationToken cancellationToken = default);
}
