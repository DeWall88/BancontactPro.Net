using BancontactPro.Helpers;

namespace BancontactPro.Net.Tests.Helpers;

/// <summary>A provider whose credentials a test can change between calls, as a database-backed one would.</summary>
internal sealed class FakeCredentialsProvider : IBancontactCredentialsProvider
{
    public FakeCredentialsProvider(BancontactCredentials? credentials = null)
    {
        Credentials = credentials ?? new BancontactCredentials();
    }

    public BancontactCredentials Credentials { get; set; }

    public int Calls { get; private set; }

    public Task<BancontactCredentials> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Credentials);
    }

    public static BancontactCredentials With(
        BancontactEnvironment environment = BancontactEnvironment.Preprod,
        BancontactSigningKey? signingKey = null,
        params (BancontactProduct Product, string ApiKey, string Ppid)[] products) => new()
    {
        Environment = environment,
        SigningKey = signingKey,
        Products = products.ToDictionary(
            p => p.Product,
            p => new BancontactProductCredentials { ApiKey = p.ApiKey, MerchantProfileId = p.Ppid }),
    };
}
