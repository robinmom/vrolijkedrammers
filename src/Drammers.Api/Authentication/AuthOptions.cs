namespace Drammers.Api.Authentication;

/// <summary>Instellingen voor Entra External ID (app settings <c>Auth__*</c>, gezet door Bicep; B-02).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Authority van de External ID-tenant, bijv. <c>https://vrolijkedrammersapp.ciamlogin.com/{tenant-id}/v2.0</c>.</summary>
    public string? Authority { get; set; }

    /// <summary>Client-ID van de API-app-registratie van deze omgeving; tokens voor een andere omgeving worden geweigerd.</summary>
    public string? Audience { get; set; }

    /// <summary>Naam van de claim met de omgevingen waarop de gebruiker mag inloggen.</summary>
    public string EnvironmentAccessClaim { get; set; } = "environmentAccess";

    /// <summary>Vereiste waarde in <see cref="EnvironmentAccessClaim"/> (<c>dev</c> of <c>acc</c>); leeg in Production.</summary>
    public string? RequiredEnvironmentAccess { get; set; }

    /// <summary>Client-ID van de app-registratie "DVD App" (public client, fase 9).</summary>
    public string? MobileClientId { get; set; }

    /// <summary>
    /// Alleen Dev/Acc: doorstuurpagina <c>/app/auth-redirect</c> voor Expo Go, dat geen eigen URL-schema heeft. In
    /// Production gebruikt de app <c>drammers://auth</c>.
    /// </summary>
    public bool MobileRedirectBridge { get; set; }
}
