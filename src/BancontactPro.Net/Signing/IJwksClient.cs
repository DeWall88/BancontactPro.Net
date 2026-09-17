using System.Security.Cryptography;

namespace BancontactPro.Signing;

/// <summary>Resolves Bancontact's public keys by <c>kid</c>, for verifying inbound callbacks.</summary>
public interface IJwksClient
{
    /// <summary>
    /// Returns the public key registered under <paramref name="kid"/>, or <c>null</c> if no such
    /// key exists (even after a cache refresh to account for key rotation).
    /// </summary>
    Task<ECDsa?> GetKeyAsync(string kid, CancellationToken cancellationToken = default);
}
