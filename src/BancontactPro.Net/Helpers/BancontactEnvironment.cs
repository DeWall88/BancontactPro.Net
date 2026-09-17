namespace BancontactPro.Helpers;

/// <summary>Selects which Bancontact Pro environment a client talks to.</summary>
public enum BancontactEnvironment
{
    /// <summary>The pre-production/sandbox environment (<c>*.preprod.bancontact.net</c>).</summary>
    Preprod,

    /// <summary>The production environment (<c>*.bancontact.net</c>).</summary>
    Production,
}
