namespace BancontactPro.Models;

/// <summary>The channel a reconciled transaction was made through.</summary>
public enum PaymentChannel
{
    /// <summary>An online/e-commerce payment.</summary>
    ONLINE,

    /// <summary>An in-person/POS payment.</summary>
    INSTORE,

    /// <summary>An invoice payment.</summary>
    INVOICE,
}
