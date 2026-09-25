using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Import.Sync;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Members;

/// <summary>Lokale velden die het bestuur mag wijzigen; e-Boekhouden-velden zijn read-only (docs/04 §4).</summary>
public sealed record MemberLocalUpdate(
    MembershipStatus? LocalStatusOverride,
    DateOnly? MembershipValidFrom,
    DateOnly? MembershipValidTo,
    string? FirstName,
    string? NamePrefix,
    string? LastName,
    DateOnly? BirthDate,
    short? JoinYear);

public sealed record MemberPurgeResult(int Members, int SyncJobs, int UnlinkedAccounts);

/// <summary>Instellingen rond testdata: leegmaken mag alleen in Dev en Acc (afgeleid van de omgeving).</summary>
public sealed class MemberDataOptions
{
    public bool AllowPurge { get; set; }
}

/// <summary>Ledenbeheer in het portal (fase 8): lokale velden, bevestigen van verdwenen leden, conflicten en opruimen.</summary>
public sealed class MemberAdministration(
    DrammersDbContext db, IAuditLogger audit, IClock clock, MemberSyncSettings settings, ConfigurationAdministration configuration,
    IOptions<MemberDataOptions> dataOptions)
{
    /// <summary>Deze tekst moet letterlijk worden meegestuurd om alle leden te verwijderen.</summary>
    public const string PurgeConfirmation = "LEDEN VERWIJDEREN";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task UpdateLocalAsync(Guid id, MemberLocalUpdate update, CancellationToken cancellationToken)
    {
        var member = await FindAsync(id, cancellationToken);
        if (update.MembershipValidFrom is { } from && update.MembershipValidTo is { } to && to < from)
        {
            throw new DomainException(ErrorCodes.Validation, "De einddatum ligt vóór de begindatum.");
        }

        if (update.LocalStatusOverride == MembershipStatus.Active)
        {
            throw new DomainException(ErrorCodes.Validation, "Een override is alleen voor Geschorst, Overleden of Inactief; laat hem leeg om de status uit e-Boekhouden te volgen.");
        }

        var mapping = await settings.GetMappingAsync(cancellationToken);
        if (update.BirthDate != member.BirthDate && mapping.BirthDate is not null)
        {
            throw new DomainException(ErrorCodes.MemberFieldFromSource, "De geboortedatum komt uit e-Boekhouden en is daar te wijzigen.");
        }

        if (update.JoinYear != member.JoinYear && mapping.JoinYear is not null)
        {
            throw new DomainException(ErrorCodes.MemberFieldFromSource, "Het inschrijfjaar komt uit e-Boekhouden en is daar te wijzigen.");
        }

        var before = Snapshot(member);
        var nameChanged = update.FirstName != member.FirstName || update.NamePrefix != member.NamePrefix || update.LastName != member.LastName;
        member.LocalStatusOverride = update.LocalStatusOverride;
        member.MembershipValidFrom = update.MembershipValidFrom;
        member.MembershipValidTo = update.MembershipValidTo;
        member.BirthDate = update.BirthDate;
        member.JoinYear = update.JoinYear;
        if (nameChanged)
        {
            member.FirstName = Clean(update.FirstName);
            member.NamePrefix = Clean(update.NamePrefix);
            member.LastName = Clean(update.LastName);
            member.NameCorrectedManually = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.updated", "Member", id.ToString(), before, Snapshot(member)), cancellationToken);
    }

    /// <summary>Een lid dat uit e-Boekhouden verdwenen is, direct op inactief zetten (in plaats van de volgende run af te wachten).</summary>
    public async Task ConfirmInactiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var member = await FindAsync(id, cancellationToken);
        if (member.SyncState != MemberSyncState.Missing)
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen een lid dat in e-Boekhouden ontbreekt, kan zo op inactief worden gezet.");
        }

        member.MembershipStatus = MembershipStatus.Inactive;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.confirmed-inactive", "Member", id.ToString()), cancellationToken);
    }

    public async Task ResolveConflictAsync(Guid id, SyncConflictStatus resolution, string? note, Guid? resolvedBy, CancellationToken cancellationToken)
    {
        if (resolution == SyncConflictStatus.Open)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies Geaccepteerd of Genegeerd.");
        }

        var conflict = await db.SyncConflicts.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Conflict niet gevonden.", DomainErrorKind.NotFound);
        if (conflict.Status != SyncConflictStatus.Open)
        {
            throw new DomainException(ErrorCodes.Validation, "Dit conflict is al afgehandeld.", DomainErrorKind.Conflict);
        }

        conflict.Status = resolution;
        conflict.ResolutionNote = Clean(note);
        conflict.ResolvedBy = resolvedBy;
        conflict.ResolvedAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member-sync.conflict-resolved", "SyncConflict", id.ToString(), null,
            JsonSerializer.Serialize(new { type = conflict.Type.ToString(), resolution = resolution.ToString() }, Json)), cancellationToken);
    }

    /// <summary>
    /// Verwijdert alle leden, syncruns en conflicten en ontkoppelt app-accounts; zet de nachtelijke sync uit zodat de
    /// leden niet vanzelf terugkomen. Alleen in Dev en Acc (besluit 2026-09-25: echte leden tijdelijk in Dev).
    /// </summary>
    public async Task<MemberPurgeResult> PurgeAsync(string confirmation, CancellationToken cancellationToken)
    {
        if (!dataOptions.Value.AllowPurge)
        {
            throw new DomainException(ErrorCodes.PurgeNotAllowed, "Leden verwijderen kan alleen in de test- en acceptatieomgeving.", DomainErrorKind.Conflict);
        }

        if (confirmation != PurgeConfirmation)
        {
            throw new DomainException(ErrorCodes.Validation, $"Typ ter bevestiging \"{PurgeConfirmation}\".");
        }

        if (await db.SyncJobs.AnyAsync(j => j.Status == SyncJobStatus.Queued || j.Status == SyncJobStatus.Running, cancellationToken))
        {
            throw new DomainException(ErrorCodes.SyncAlreadyRunning, "Er loopt een ledensync. Wacht tot die klaar is.", DomainErrorKind.Conflict);
        }

        await configuration.SetFeatureFlagAsync(MemberSyncSettings.ScheduleFlag, false, "Uitgezet bij het verwijderen van alle leden", cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var unlinked = await db.Users.Where(u => u.MemberId != null).ExecuteUpdateAsync(s => s.SetProperty(u => u.MemberId, (Guid?)null), cancellationToken);
        await db.SyncConflicts.ExecuteDeleteAsync(cancellationToken);
        await db.SyncJobItems.ExecuteDeleteAsync(cancellationToken);
        var jobs = await db.SyncJobs.ExecuteDeleteAsync(cancellationToken);
        var members = await db.Members.ExecuteDeleteAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.purged", "Member", "*", null,
            JsonSerializer.Serialize(new { members, syncJobs = jobs, unlinkedAccounts = unlinked }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MemberPurgeResult(members, jobs, unlinked);
    }

    private async Task<Member> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Members.SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
        ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);

    /// <summary>Alleen lokale velden in de auditlog (geen NAW uit e-Boekhouden).</summary>
    private static string Snapshot(Member m) => JsonSerializer.Serialize(new
    {
        localStatusOverride = m.LocalStatusOverride?.ToString(),
        m.MembershipValidFrom,
        m.MembershipValidTo,
        m.NameCorrectedManually,
        birthDateSet = m.BirthDate is not null,
        m.JoinYear,
    }, Json);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
