using Microsoft.Extensions.Options;

namespace BancontactPro.Helpers;

/// <summary>
/// The default <see cref="IBancontactCredentialsProvider"/>: reads <see cref="BancontactProOptions"/>
/// (bound from the <c>BancontactPro</c> configuration section by <c>AddBancontactProIntegration</c>).
/// The options are read once, so this provider suits static settings. It also shows how a provider
/// maps a host's settings onto a <see cref="BancontactCredentials"/>.
/// </summary>
public sealed class ConfigurationCredentialsProvider : IBancontactCredentialsProvider
{
    private readonly IOptions<BancontactProOptions> _options;
    private BancontactCredentials? _credentials;

    /// <param name="options">The bound and validated Bancontact options.</param>
    public ConfigurationCredentialsProvider(IOptions<BancontactProOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public Task<BancontactCredentials> GetCredentialsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_credentials ??= Build(_options.Value));

    private static BancontactCredentials Build(BancontactProOptions options) => new()
    {
        Environment = options.Environment,
        Products = new Dictionary<BancontactProduct, BancontactProductCredentials>(options.Products),
        SigningKey = options.HasSigningKey ? new BancontactSigningKey(options.Kid!, options.PrivateKeyPem!) : null,
        Issuer = options.Issuer,
    };
}
