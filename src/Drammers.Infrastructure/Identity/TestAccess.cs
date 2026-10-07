using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Identity;

/// <summary>Instellingen voor de knop "Toegang tot testomgeving" (alleen Dev/Acc).</summary>
public sealed class TestAccessOptions
{
    /// <summary>De omgeving waarin de API draait (<c>Auth:RequiredEnvironmentAccess</c>): <c>dev</c>, <c>acc</c> of leeg (Prod).</summary>
    public string? Environment { get; set; }

    /// <summary>Waarde van environmentAccess bij toegang geven (<c>Graph:TestAccessEnvironments</c>).</summary>
    public string Grant { get; set; } = "dev";
}

public sealed record TestAccessStatus(
    bool Available,
    string? Environment,
    string Grant,
    bool HasSignIn,
    bool InTestersGroup,
    string? Environments,
    bool HasAccessHere);

/// <summary>
/// Testtoegang vanuit het portal (B-02): zet een inlog in de groep Testers en vult het attribuut environmentAccess, zoals
/// <c>infra/entra/set-tester.sh</c>. Alleen in Dev/Acc en alleen als Graph daarvoor is ingesteld; in Prod is er geen
/// toewijzingsplicht en dus niets te doen.
/// </summary>
public sealed class TestAccessAdministration(
    DrammersDbContext db,
    IEntraUserDirectory entra,
    IOptions<GraphOptions> graph,
    IOptions<TestAccessOptions> options,
    IAuditLogger audit)
{
    private bool Available => !string.IsNullOrWhiteSpace(options.Value.Environment) && graph.Value.TestAccessConfigured;

    public async Task<TestAccessStatus> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!Available)
        {
            return new TestAccessStatus(false, settings.Environment, settings.Grant, false, false, null, false);
        }

        var objectId = await ResolveObjectIdAsync(await FindUserAsync(userId, cancellationToken), cancellationToken);
        if (objectId is null)
        {
            return new TestAccessStatus(true, settings.Environment, settings.Grant, false, false, null, false);
        }

        var access = await GraphAsync(() => entra.GetTestAccessAsync(objectId, cancellationToken));
        var here = access.InTestersGroup && (access.Environments ?? "").Split(',', StringSplitOptions.TrimEntries).Contains(settings.Environment, StringComparer.OrdinalIgnoreCase);
        return new TestAccessStatus(true, settings.Environment, settings.Grant, true, access.InTestersGroup, access.Environments, here);
    }

    public async Task<TestAccessStatus> SetAsync(Guid userId, bool grant, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            throw new DomainException(ErrorCodes.TestAccessUnavailable, "Testtoegang is alleen in Dev en Acc beschikbaar, en Graph is daarvoor niet ingesteld.", DomainErrorKind.Conflict);
        }

        var user = await FindUserAsync(userId, cancellationToken);
        var objectId = await ResolveObjectIdAsync(user, cancellationToken)
            ?? throw new DomainException(
                ErrorCodes.NoSignIn,
                "Er is nog geen inlog voor dit e-mailadres. Laat de persoon eerst in de app of het portal een inlog aanmaken met e-mail + code (de foutmelding daarna is normaal) en probeer het dan opnieuw.",
                DomainErrorKind.Conflict);

        await GraphAsync(async () =>
        {
            await entra.SetTestAccessAsync(objectId, grant ? options.Value.Grant : null, cancellationToken);
            if (!grant)
            {
                await entra.RevokeSessionsAsync(objectId, cancellationToken);
            }

            return true;
        });

        await audit.WriteAsync(
            new AuditEntry(grant ? "user.test-access-granted" : "user.test-access-revoked", "User", user.Id.ToString(), null, grant ? $"{{\"environments\":\"{options.Value.Grant}\"}}" : null),
            cancellationToken);
        return await GetAsync(userId, cancellationToken);
    }

    /// <summary>Ontbrekende Graph-rechten zijn een instellingsfout die het bestuur moet kunnen lezen, geen 500.</summary>
    private static async Task<T> GraphAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            throw new DomainException(
                ErrorCodes.TestAccessUnavailable,
                "De provisioning-app mag de groep Testers niet aanpassen (Graph-recht GroupMember.ReadWrite.All ontbreekt). Voer infra/entra/register-provisioning-app.sh opnieuw uit.",
                DomainErrorKind.Conflict);
        }
    }

    private async Task<User> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.AccountStatus != AccountStatus.Deleted, cancellationToken)
        ?? throw new DomainException(ErrorCodes.UserNotFound, "Gebruiker niet gevonden.", DomainErrorKind.NotFound);

    /// <summary>De gekoppelde inlog, of (nog nooit aangemeld) de inlog die al bij het e-mailadres hoort.</summary>
    private async Task<string?> ResolveObjectIdAsync(User user, CancellationToken cancellationToken) =>
        PendingObjectId.IsPending(user.ExternalObjectId)
            ? await entra.FindByEmailAsync(user.Email, cancellationToken)
            : user.ExternalObjectId;
}
