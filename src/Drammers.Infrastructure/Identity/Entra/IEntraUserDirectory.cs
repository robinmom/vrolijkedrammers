namespace Drammers.Infrastructure.Identity.Entra;

/// <summary>
/// Accounts in Entra External ID via Microsoft Graph (ADR-014). Alleen de provisioning maakt accounts; zelfregistratie
/// staat uit.
/// </summary>
public interface IEntraUserDirectory
{
    /// <returns>De <c>oid</c> van het account met dit aanmeld-e-mailadres, of <c>null</c>.</returns>
    Task<string?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <returns>De <c>oid</c> van het nieuwe account (aanmelden met e-mail + eenmalige code).</returns>
    Task<string> CreateAsync(string email, string displayName, CancellationToken cancellationToken);

    Task SetAccountEnabledAsync(string objectId, bool enabled, CancellationToken cancellationToken);

    /// <summary>Trekt alle refresh-tokens en sessies in; lopende access tokens verlopen binnen hun looptijd.</summary>
    Task RevokeSessionsAsync(string objectId, CancellationToken cancellationToken);
}

/// <summary>Instellingen voor Graph in de External ID-tenant (app settings <c>Graph__*</c>).</summary>
public sealed class GraphOptions
{
    public const string SectionName = "Graph";

    public string? TenantId { get; set; }

    /// <summary>Client-ID van de provisioning-app-registratie in de External ID-tenant.</summary>
    public string? ClientId { get; set; }

    /// <summary>Issuer van lokale accounts, bijv. <c>vrolijkedrammersapp.onmicrosoft.com</c>.</summary>
    public string? IssuerDomain { get; set; }

    /// <summary>
    /// Naam van het Key Vault-certificaat waarmee de provisioning-app zich aanmeldt. Federatie met de managed identity
    /// is niet mogelijk naar een external tenant (AADSTS700236); het certificaat verlaat Key Vault niet.
    /// </summary>
    public string? CertificateName { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(IssuerDomain) && !string.IsNullOrWhiteSpace(CertificateName);
}
