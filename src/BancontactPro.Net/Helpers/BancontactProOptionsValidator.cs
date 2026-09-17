using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace BancontactPro.Helpers;

internal sealed class BancontactProOptionsValidator : IValidateOptions<BancontactProOptions>
{
    public ValidateOptionsResult Validate(string? name, BancontactProOptions options)
    {
        List<string> failures = [];

        Require(options.ApiKey, nameof(BancontactProOptions.ApiKey), failures);
        Require(options.MerchantProfileId, nameof(BancontactProOptions.MerchantProfileId), failures);
        Require(options.Kid, nameof(BancontactProOptions.Kid), failures);

        if (!Require(options.PrivateKeyPem, nameof(BancontactProOptions.PrivateKeyPem), failures))
        {
            ValidatePrivateKeyPem(options.PrivateKeyPem!, failures);
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
