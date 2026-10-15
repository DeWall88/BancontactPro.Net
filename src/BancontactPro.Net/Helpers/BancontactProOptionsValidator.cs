using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace BancontactPro.Helpers;

internal sealed class BancontactProOptionsValidator : IValidateOptions<BancontactProOptions>
{
    public ValidateOptionsResult Validate(string? name, BancontactProOptions options)
    {
        List<string> failures = [];

        // A product's API key and PPID are a pair: half a pair is a configuration mistake worth failing
        // on at startup. An entry with both values empty means "not used" (a deployment can declare
        // every product's variables and leave the unused ones blank).
        foreach (var (product, credentials) in options.Products)
        {
            if (credentials.IsEmpty)
            {
                continue;
            }

            Require(credentials.ApiKey, $"Products:{product}:ApiKey", failures);
            Require(credentials.MerchantProfileId, $"Products:{product}:MerchantProfileId", failures);
        }

        if (options.Products.Values.All(credentials => credentials.IsEmpty))
        {
            failures.Add($"BancontactPro configuration needs at least one product under 'Products' " +
                         $"({string.Join(", ", Enum.GetNames<BancontactProduct>())}), each with an ApiKey and a MerchantProfileId.");
        }

        // The signing key is optional (only Refund and Reconciliation need it), but all-or-nothing.
        if (!string.IsNullOrWhiteSpace(options.Kid) || !string.IsNullOrWhiteSpace(options.PrivateKeyPem))
        {
            Require(options.Kid, nameof(BancontactProOptions.Kid), failures);

            if (!Require(options.PrivateKeyPem, nameof(BancontactProOptions.PrivateKeyPem), failures))
            {
                ValidatePrivateKeyPem(options.PrivateKeyPem!, failures);
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool Require(string? value, string keyName, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"BancontactPro configuration value '{keyName}' is required.");
            return true;
        }

        return false;
    }

    private static void ValidatePrivateKeyPem(string pem, List<string> failures)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            failures.Add($"BancontactPro configuration value '{nameof(BancontactProOptions.PrivateKeyPem)}' is not a valid PEM-encoded EC private key.");
        }
    }
}
