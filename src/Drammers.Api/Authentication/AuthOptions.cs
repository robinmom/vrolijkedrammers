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

    /// <summary>Client-ID van de portal-app; tokens van die client moeten MFA bevatten als <see cref="RequirePortalMfa"/> aan staat.</summary>
    public string? PortalClientId { get; set; }

    /// <summary>
    /// Tweede slot naast de Conditional Access-policy op de portal-app (B-02): <c>amr</c> moet <c>mfa</c> bevatten.
    /// Pas aanzetten nadat met een echt token is gecontroleerd dat External ID de claim meestuurt.
    /// </summary>
    public bool RequirePortalMfa { get; set; }
}
