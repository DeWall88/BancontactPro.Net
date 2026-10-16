using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BancontactPro.Helpers;
using BancontactPro.Signing;
using BancontactPro.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BancontactPro.Net.Tests.Helpers;

/// <summary>Registration with a credentials provider (<c>AddBancontactPro</c>): nothing at startup, everything per call.</summary>
public class BancontactProProviderRegistrationTests
{
    /// <summary>Records every request and serves a JWKS per JWKS host, so the key sets of two environments differ.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public HttpRequestMessage? LastRequest => Requests.LastOrDefault();

        public Dictionary<string, object> JwksByHost { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (JwksByHost.TryGetValue(request.RequestUri!.Host, out var jwks))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(jwks) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private static string GenerateValidPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    private static ServiceProvider Build(Action<IServiceCollection> register, RoutingHandler handler)
    {
        var services = new ServiceCollection();
        register(services);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        // Strict container: a singleton that captured a scoped provider, or a service that can't be built, fails here.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static ServiceProvider Build(FakeCredentialsProvider fake, RoutingHandler handler, ServiceLifetime lifetime = ServiceLifetime.Singleton) =>
        Build(services => services.AddBancontactPro(_ => fake, lifetime), handler);

    private static JoseHeaderView SignatureOf(HttpRequestMessage request)
    {
        var signature = request.Headers.GetValues("Signature").Single();
        var json = JsonDocument.Parse(Base64Url.DecodeFromChars(signature.Split('.')[0]));
        return new JoseHeaderView(
            json.RootElement.GetProperty("kid").GetString()!,
            json.RootElement.GetProperty("https://payconiq.com/sub").GetString()!,
            json.RootElement.GetProperty("https://payconiq.com/iss").GetString()!);
    }

    private sealed record JoseHeaderView(string Kid, string Sub, string Iss);

    [Fact]
    public async Task NothingConfigured_HostStartsAndEveryServiceResolves()
    {
        var handler = new RoutingHandler();
        var fake = new FakeCredentialsProvider(); // no product, no signing key, nothing
        using var provider = Build(fake, handler);

        // What a host does at startup: validate (there is nothing registered to fail) and resolve the services.
        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.NotNull(provider.GetRequiredService<IBancontactClientFactory>());
        Assert.NotNull(provider.GetRequiredService<IJwksClient>());
        Assert.NotNull(provider.GetRequiredService<BancontactCallbackVerifier>());

        // Nothing was read to get there, and no options object is required.
        Assert.Equal(0, fake.Calls);
        Assert.Empty(handler.Requests);

        foreach (var product in Enum.GetValues<BancontactProduct>())
        {
            Assert.False(await provider.GetRequiredService<IBancontactClientFactory>().IsConfiguredAsync(product));
        }
    }

    [Fact]
    public async Task NothingConfigured_ClientsReportNotConfiguredOnlyWhenUsed()
    {
        var handler = new RoutingHandler();
        using var provider = Build(new FakeCredentialsProvider(), handler);
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        var payments = await Assert.ThrowsAsync<BancontactProductNotConfiguredException>(() => factory.PaymentsAsync(BancontactProduct.StaticQr));
        await Assert.ThrowsAsync<BancontactProductNotConfiguredException>(() => factory.RefundsAsync(BancontactProduct.StaticQr));
        await Assert.ThrowsAsync<BancontactProductNotConfiguredException>(() => factory.ReconciliationAsync(BancontactProduct.StaticQr));

        Assert.Equal(BancontactProduct.StaticQr, payments.Product);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MissingProduct_ThrowsOnlyForThatProduct()
    {
        var handler = new RoutingHandler();
        var fake = new FakeCredentialsProvider(FakeCredentialsProvider.With(products: (BancontactProduct.StaticQr, "static-key", "static-ppid")));
        using var provider = Build(fake, handler);
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        await (await factory.PaymentsAsync(BancontactProduct.StaticQr)).CancelAsync("p1");
        await Assert.ThrowsAsync<BancontactProductNotConfiguredException>(() => factory.PaymentsAsync(BancontactProduct.OnDisplay));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task HalfAPair_CountsAsNotConfigured()
    {
        var handler = new RoutingHandler();
        var fake = new FakeCredentialsProvider(new BancontactCredentials
        {
            Products = new Dictionary<BancontactProduct, BancontactProductCredentials>
            {
                [BancontactProduct.OnDisplay] = new() { ApiKey = "key-only" },
            },
        });
        using var provider = Build(fake, handler);

        Assert.False(await provider.GetRequiredService<IBancontactClientFactory>().IsConfiguredAsync(BancontactProduct.OnDisplay));
        await Assert.ThrowsAsync<BancontactProductNotConfiguredException>(() =>
            provider.GetRequiredService<IBancontactClientFactory>().PaymentsAsync(BancontactProduct.OnDisplay));
    }

    [Fact]
    public async Task MissingSigningKey_ThrowsOnlyForSignedClients()
    {
        var handler = new RoutingHandler();
        var fake = new FakeCredentialsProvider(FakeCredentialsProvider.With(products: (BancontactProduct.StaticQr, "static-key", "static-ppid")));
        using var provider = Build(fake, handler);
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        await (await factory.PaymentsAsync(BancontactProduct.StaticQr)).CancelAsync("p1");
        await Assert.ThrowsAsync<BancontactSigningKeyNotConfiguredException>(() => factory.RefundsAsync(BancontactProduct.StaticQr));
        await Assert.ThrowsAsync<BancontactSigningKeyNotConfiguredException>(() => factory.ReconciliationAsync(BancontactProduct.StaticQr));
    }

    [Fact]
    public async Task UnparseableSigningKey_FailsWhenASignedClientIsRequested()
    {
        var handler = new RoutingHandler();
        var fake = new FakeCredentialsProvider(FakeCredentialsProvider.With(
            signingKey: new BancontactSigningKey("kid-1", "not a pem"),
            products: (BancontactProduct.StaticQr, "static-key", "static-ppid")));
        using var provider = Build(fake, handler);
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.RefundsAsync(BancontactProduct.StaticQr));
        Assert.Contains("kid-1", failure.Message);
        Assert.DoesNotContain("not a pem", failure.Message);
        await (await factory.PaymentsAsync(BancontactProduct.StaticQr)).CancelAsync("p1"); // payments are unaffected
    }

    [Fact]
    public async Task ChangedApiKey_IsUsedByTheNextCall()
    {
        var handler = new RoutingHandler();
        var fake = new FakeCredentialsProvider(FakeCredentialsProvider.With(products: (BancontactProduct.OnDisplay, "key-1", "ppid-1")));
        using var provider = Build(fake, handler);
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        await (await factory.PaymentsAsync(BancontactProduct.OnDisplay)).CancelAsync("p1");
        fake.Credentials = FakeCredentialsProvider.With(products: (BancontactProduct.OnDisplay, "key-2", "ppid-2"));
        await (await factory.PaymentsAsync(BancontactProduct.OnDisplay)).CancelAsync("p1");

        Assert.Equal(["key-1", "key-2"], handler.Requests.Select(r => r.Headers.Authorization?.Parameter));
    }

    [Fact]
    public async Task ChangedSigningKeyAndPpid_AreUsedByTheNextSignedCall()
    {
        var handler = new RoutingHandler();
        var pem1 = GenerateValidPem();
        var pem2 = GenerateValidPem();
        var fake = new FakeCredentialsProvider(FakeCredentialsProvider.With(
            signingKey: new BancontactSigningKey("kid-1", pem1),
            products: (BancontactProduct.OnDisplay, "key-1", "ppid-1")));
        using var provider = Build(fake, handler);
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        await TryGetRefundAsync(factory);
        fake.Credentials = FakeCredentialsProvider.With(
            signingKey: new BancontactSigningKey("kid-2", pem2),
            products: (BancontactProduct.OnDisplay, "key-2", "ppid-2"));
        await TryGetRefundAsync(factory);

        Assert.Equal([new JoseHeaderView("kid-1", "ppid-1", "Payconiq"), new JoseHeaderView("kid-2", "ppid-2", "Payconiq")],
            handler.Requests.Select(SignatureOf));
        Assert.Equal(["key-1", "key-2"], handler.Requests.Select(r => r.Headers.Authorization?.Parameter));

        static async Task TryGetRefundAsync(IBancontactClientFactory factory)
        {
            try
            {
                await (await factory.RefundsAsync(BancontactProduct.OnDisplay)).GetAsync("p1", "r1");
            }
            catch (Exception)
            {
                // The canned empty response doesn't deserialize; only the outgoing request matters here.
            }
        }
    }

    [Fact]
    public async Task ChangedIssuer_IsUsedByTheNextSignedCall()
    {
        var handler = new RoutingHandler();
        var fake = new FakeCredentialsProvider(FakeCredentialsProvider.With(
            signingKey: new BancontactSigningKey("kid-1", GenerateValidPem()),
            products: (BancontactProduct.OnDisplay, "key-1", "ppid-1")) with { Issuer = "merchant-42" });
        using var provider = Build(fake, handler);

        try
        {
            await (await provider.GetRequiredService<IBancontactClientFactory>().ReconciliationAsync(BancontactProduct.OnDisplay)).GetPayoutsAsync();
        }
        catch (Exception)
        {
            // Only the outgoing request matters here.
        }

        Assert.Equal("merchant-42", SignatureOf(handler.LastRequest!).Iss);
    }

    [Fact]
    public async Task SwitchingEnvironment_ChangesTheBaseAddressOfTheNextCall()
    {
        var handler = new RoutingHandler();
        var fake = new FakeCredentialsProvider(FakeCredentialsProvider.With(BancontactEnvironment.Preprod, products: (BancontactProduct.OnDisplay, "key-1", "ppid-1")));
        using var provider = Build(fake, handler);
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        await (await factory.PaymentsAsync(BancontactProduct.OnDisplay)).CancelAsync("p1");
        fake.Credentials = FakeCredentialsProvider.With(BancontactEnvironment.Production, products: (BancontactProduct.OnDisplay, "key-1", "ppid-1"));
        await (await factory.PaymentsAsync(BancontactProduct.OnDisplay)).CancelAsync("p1");

        Assert.Equal(["merchant.api.preprod.bancontact.net", "merchant.api.bancontact.net"], handler.Requests.Select(r => r.RequestUri!.Host));
        Assert.Equal(BancontactEndpoints.GetMerchantApiBaseUri(BancontactEnvironment.Production).Host, handler.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public async Task SwitchingEnvironment_ChangesTheJwksEndpointAndKeepsOneCachePerEnvironment()
    {
        using var preprodKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var productionKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = new RoutingHandler();
        handler.JwksByHost["jwks.preprod.bancontact.net"] = new { keys = new[] { JwkFor(preprodKey, "preprod-kid") } };
        handler.JwksByHost["jwks.bancontact.net"] = new { keys = new[] { JwkFor(productionKey, "production-kid") } };
        var fake = new FakeCredentialsProvider(new BancontactCredentials { Environment = BancontactEnvironment.Preprod });
        using var provider = Build(fake, handler);
        var jwks = provider.GetRequiredService<IJwksClient>();

        Assert.NotNull(await jwks.GetKeyAsync("preprod-kid"));
        Assert.Equal("jwks.preprod.bancontact.net", handler.LastRequest!.RequestUri!.Host);

        fake.Credentials = new BancontactCredentials { Environment = BancontactEnvironment.Production };
        Assert.Null(await jwks.GetKeyAsync("preprod-kid")); // not served by the production key set
        Assert.Equal("jwks.bancontact.net", handler.LastRequest.RequestUri!.Host);
        Assert.NotNull(await jwks.GetKeyAsync("production-kid"));

        // Switching back reuses the cached Preprod keys.
        var requests = handler.Requests.Count;
        fake.Credentials = new BancontactCredentials { Environment = BancontactEnvironment.Preprod };
        Assert.NotNull(await jwks.GetKeyAsync("preprod-kid"));
        Assert.Equal(requests, handler.Requests.Count);
    }

    [Fact]
    public async Task TheJwksClient_IsOneLongLivedInstanceSharedByEveryConsumer()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = new RoutingHandler();
        handler.JwksByHost["jwks.preprod.bancontact.net"] = new { keys = new[] { JwkFor(key, "kid-1") } };
        using var provider = Build(new FakeCredentialsProvider(), handler);

        var first = provider.GetRequiredService<IJwksClient>();
        using var scope = provider.CreateScope();
        var second = scope.ServiceProvider.GetRequiredService<IJwksClient>();
        Assert.Same(first, second);
        Assert.IsType<JwksClient>(first);

        // One cache: the second lookup, through the verifier's own reference, is not fetched again.
        await first.GetKeyAsync("kid-1");
        await second.GetKeyAsync("kid-1");
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ScopedProvider_IsResolvedPerCallAndNeverCapturedByTheSingletons()
    {
        var handler = new RoutingHandler();
        var created = new List<ScopedProvider>();
        using var provider = Build(services =>
        {
            services.AddSingleton(created);
            services.AddScoped<ScopedDependency>();
            services.AddBancontactPro<ScopedProvider>(); // default: Scoped
        }, handler);
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        await (await factory.PaymentsAsync(BancontactProduct.OnDisplay)).CancelAsync("p1");
        await (await factory.PaymentsAsync(BancontactProduct.OnDisplay)).CancelAsync("p1");

        Assert.Equal(2, created.Count);
        Assert.NotSame(created[0], created[1]);
        Assert.Equal(["scoped-key", "scoped-key"], handler.Requests.Select(r => r.Headers.Authorization?.Parameter));
    }

    [Fact]
    public void GenericRegistration_UsesTheGivenProviderType()
    {
        using var provider = Build(services => services.AddBancontactPro<ScopedProvider>(ServiceLifetime.Singleton)
            .AddSingleton<List<ScopedProvider>>().AddSingleton<ScopedDependency>(), new RoutingHandler());

        Assert.IsType<ScopedProvider>(provider.GetRequiredService<IBancontactCredentialsProvider>());
    }

    private sealed class ScopedDependency;

    private sealed class ScopedProvider : IBancontactCredentialsProvider
    {
        public ScopedProvider(ScopedDependency dependency, List<ScopedProvider> created)
        {
            _ = dependency;
            created.Add(this);
        }

        public Task<BancontactCredentials> GetCredentialsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(FakeCredentialsProvider.With(products: (BancontactProduct.OnDisplay, "scoped-key", "scoped-ppid")));
    }

    private static object JwkFor(ECDsa key, string kid)
    {
        var p = key.ExportParameters(includePrivateParameters: false);
        return new { kty = "EC", crv = "P-256", kid, x = Base64Url.EncodeToString(p.Q.X!), y = Base64Url.EncodeToString(p.Q.Y!) };
    }
}
