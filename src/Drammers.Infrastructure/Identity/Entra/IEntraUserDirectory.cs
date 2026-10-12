namespace Drammers.Infrastructure.Identity.Entra;

/// <summary>
/// Accounts in Entra External ID via Microsoft Graph (ADR-014, herzien 2026-09-27). De API maakt zelf geen inlogaccounts
/// meer: Graph eist daarbij een wachtwoord, en zo'n account krijgt altijd de wachtwoordpagina. Leden en beheerders maken
/// hun inlog zelf aan met e-mail + eenmalige code; de API koppelt die alleen aan een goedgekeurd account
/// (<see cref="Identity.AccountLinker"/>).
/// </summary>
public interface IEntraUserDirectory
{
    /// <returns>De <c>oid</c> van het account met dit aanmeld-e-mailadres, of <c>null</c>.</returns>
    Task<string?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <returns>
    /// Het (door de eenmalige code geverifieerde) aanmeld-e-mailadres van dit account, of <c>null</c> als het account niet
    /// (meer) bestaat.
    /// </returns>
    Task<string?> GetSignInEmailAsync(string objectId, CancellationToken cancellationToken);

    Task SetAccountEnabledAsync(string objectId, bool enabled, CancellationToken cancellationToken);

    /// <summary>Trekt alle refresh-tokens en sessies in; lopende access tokens verlopen binnen hun looptijd.</summary>
    Task RevokeSessionsAsync(string objectId, CancellationToken cancellationToken);

    /// <summary>Verwijdert het account (account verwijderen door het lid); een al verwijderd account is geen fout.</summary>
    Task DeleteAsync(string objectId, CancellationToken cancellationToken);

    /// <summary>Toegang tot Dev/Acc (B-02): lid van de groep Testers en de waarde van het attribuut environmentAccess.</summary>
    Task<EntraTestAccess> GetTestAccessAsync(string objectId, CancellationToken cancellationToken);

    /// <summary>
    /// Zet het attribuut environmentAccess (bijv. <c>dev,acc</c>) en het lidmaatschap van Testers; <c>null</c> trekt beide in.
    /// Vervangt <c>infra/entra/set-tester.sh</c>.
    /// </summary>
    Task SetTestAccessAsync(string objectId, string? environments, CancellationToken cancellationToken);

    /// <summary>
    /// Inloggen met wachtwoord (2026-10-12): maakt een lokaal account (e-mail + wachtwoord) met een willekeurig wachtwoord
    /// dat niemand kent; het lid kiest de eerste keer zelf een wachtwoord via "Wachtwoord vergeten?".
    /// </summary>
    /// <returns>De <c>oid</c> van het nieuwe account.</returns>
    Task<string> CreatePasswordAccountAsync(string email, string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Zorgt dat een bestaand account (bijv. een inlog met e-mail + code) ook met e-mail + wachtwoord kan inloggen: voegt de
    /// identity toe met een willekeurig wachtwoord. Een account dat al een wachtwoordinlog heeft, blijft ongewijzigd.
    /// </summary>
    Task EnsurePasswordSignInAsync(string objectId, string email, CancellationToken cancellationToken);
}

/// <summary>Instellingen voor accounts (<c>Accounts__*</c>).</summary>
public sealed class AccountOptions
{
    public const string SectionName = "Accounts";

    /// <summary>
    /// Inloggen met e-mail + wachtwoord (user flow "DVD inloggen met wachtwoord", infra/entra/wachtwoord-inloggen.sh): de
    /// API maakt bij elk account direct het inlogaccount; zelf aanmelden en accountverzoeken kunnen niet meer. Uit: de
    /// oude manier (e-mail + code, het lid maakt zelf een inlog die de API koppelt).
    /// </summary>
    public bool PasswordSignIn { get; set; }
}

public sealed record EntraTestAccess(bool InTestersGroup, string? Environments);

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

    /// <summary>Object-id van de groep Testers (Dev/Acc: "Require user assignment"); leeg in Prod.</summary>
    public string? TestersGroupId { get; set; }

    /// <summary>Volledige naam van de directory-extensie, bijv. <c>extension_…_environmentAccess</c>; leeg in Prod.</summary>
    public string? EnvironmentAccessAttribute { get; set; }

    public bool TestAccessConfigured =>
        IsConfigured && !string.IsNullOrWhiteSpace(TestersGroupId) && !string.IsNullOrWhiteSpace(EnvironmentAccessAttribute);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(IssuerDomain) && !string.IsNullOrWhiteSpace(CertificateName);
}
