namespace BancontactPro.Webhooks;

/// <summary>
/// Thrown when an inbound Bancontact callback can't be trusted — either its signature doesn't
/// verify, or its body doesn't match the expected schema. A webhook endpoint should treat either
/// case as a rejected request (4xx), not as any kind of partial success.
/// </summary>
public sealed class InvalidCallbackException : Exception
{
    /// <param name="message">Why the callback was rejected.</param>
    public InvalidCallbackException(string message) : base(message)
    {
    }

    /// <param name="message">Why the callback was rejected.</param>
    /// <param name="innerException">The underlying cause, if any (e.g. a JSON parse failure).</param>
    public InvalidCallbackException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
