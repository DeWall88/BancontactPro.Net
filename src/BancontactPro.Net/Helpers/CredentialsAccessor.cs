using Microsoft.Extensions.DependencyInjection;

namespace BancontactPro.Helpers;

/// <summary>
/// Reads the credentials from the registered <see cref="IBancontactCredentialsProvider"/> inside a fresh DI
/// scope, so the singleton factory and JWKS client can use a provider of any lifetime (a scoped one that
/// depends on a DbContext, say).
/// </summary>
internal sealed class CredentialsAccessor
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CredentialsAccessor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<BancontactCredentials> GetAsync(CancellationToken cancellationToken)
    {
        var scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var provider = scope.ServiceProvider.GetRequiredService<IBancontactCredentialsProvider>();
            return await provider.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
