using System.Globalization;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

/// <param name="Year">Carnavalsjaar (het jaar van het carnaval, bijv. 2027).</param>
/// <param name="NewMembers">Nieuwe leden (zonder gesplitste tweede leden).</param>
/// <param name="LeftMembers">Uitgeschreven: inactief, overleden, geschorst of verwijderd.</param>
/// <param name="TotalMembers">Actieve leden aan het eind van het jaar (lopend jaar: nu).</param>
/// <param name="Manual">Welke waarden het bestuur zelf heeft ingevuld of geïmporteerd.</param>
public sealed record MemberHistoryRow(int Year, int? NewMembers, int? LeftMembers, int? TotalMembers, MemberHistoryManual Manual);

public sealed record MemberHistoryManual(bool NewMembers, bool LeftMembers, bool TotalMembers);

/// <param name="CurrentYear">Het lopende carnavalsjaar.</param>
/// <param name="Rows">Per carnavalsjaar, oudste eerst.</param>
/// <param name="TrackedSince">Vanaf dit carnavalsjaar legt het systeem uitschrijvingen en het totaal zelf vast.</param>
public sealed record MemberHistoryReport(int CurrentYear, int TrackedSince, IReadOnlyList<MemberHistoryRow> Rows);

public sealed record MemberHistoryInput(int? NewMembers, int? LeftMembers, int? TotalMembers);

public sealed record MemberHistoryImportRow(int RowNumber, string Year, string? NewMembers, string? LeftMembers, string? TotalMembers);

/// <summary>
/// Ledenverloop per carnavalsjaar (2026-10-15): nieuwe leden (uit het inschrijfjaar, zonder gesplitste tweede leden),
/// uitschrijvingen en het totaal. Uitschrijvingen en het totaal legt het systeem vanaf nu zelf vast (dagelijks); oudere
/// jaren vult het bestuur in of importeert het uit Excel, en die cijfers gaan voor.
/// </summary>
public sealed class MemberHistory(DrammersDbContext db, IClock clock, IAuditLogger audit)
{
    /// <summary>Volledig verwijderde leden van vóór deze datum (testleden bij de livegang) tellen niet als uitschrijving.</summary>
    public static readonly DateOnly TrackingStart = new(2026, 10, 15);

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    /// <summary>Legt nieuwe uitschrijvingen en het totaal van het lopende carnavalsjaar vast (dagelijkse job).</summary>
    public async Task RecordAsync(CancellationToken cancellationToken)
    {
        var today = Today;
        var year = await JoinYears.ForDateAsync(db, today, cancellationToken);
        var members = (await db.Members.AsNoTracking().Select(m => new { m.MemberNumber, m.MembershipStatus, m.LocalStatusOverride }).ToListAsync(cancellationToken))
            .Select(m => (m.MemberNumber, Status: m.LocalStatusOverride ?? m.MembershipStatus)).ToList();
        var exits = await db.MemberExits.ToListAsync(cancellationToken);
        var exited = exits.Select(e => e.MemberNumber).ToHashSet(StringComparer.Ordinal);

        foreach (var (number, status) in members)
        {
            if (status != MembershipStatus.Active && !exited.Contains(number))
            {
                db.MemberExits.Add(new MemberExit
                {
                    Id = IdGenerator.NewId(),
                    MemberNumber = number,
                    Reason = status switch { MembershipStatus.Deceased => MemberExitReason.Deceased, MembershipStatus.Suspended => MemberExitReason.Suspended, _ => MemberExitReason.Inactive },
                    ExitedOn = today,
                    CarnivalYear = year,
                });
                exited.Add(number);
            }
        }

        // Weer actief in hetzelfde carnavalsjaar: dan was het geen uitschrijving.
        var active = members.Where(m => m.Status == MembershipStatus.Active).Select(m => m.MemberNumber).ToHashSet(StringComparer.Ordinal);
        db.MemberExits.RemoveRange(exits.Where(e => e.Reason != MemberExitReason.Removed && e.CarnivalYear == year && active.Contains(e.MemberNumber)));

        var start = TrackingStart.ToDateTime(TimeOnly.MinValue);
        foreach (var removed in await db.ExcludedMembers.AsNoTracking().Where(x => x.ExcludedAt >= start).ToListAsync(cancellationToken))
        {
            if (!exited.Contains(removed.MemberNumber))
            {
                var on = DateOnly.FromDateTime(removed.ExcludedAt);
                db.MemberExits.Add(new MemberExit
                {
                    Id = IdGenerator.NewId(),
                    MemberNumber = removed.MemberNumber,
                    Reason = MemberExitReason.Removed,
                    ExitedOn = on,
                    CarnivalYear = await JoinYears.ForDateAsync(db, on, cancellationToken),
                });
            }
        }

        var stat = await db.MemberYearStats.SingleOrDefaultAsync(s => s.CarnivalYear == year, cancellationToken);
        if (stat is null)
        {
            stat = new MemberYearStat { CarnivalYear = year };
            db.MemberYearStats.Add(stat);
        }

        stat.SnapshotTotal = active.Count;
        stat.SnapshotAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MemberHistoryReport> ReportAsync(CancellationToken cancellationToken)
    {
        var current = await JoinYears.ForDateAsync(db, Today, cancellationToken);
        var trackedSince = JoinYears.ForDate(TrackingStart);
        var members = await db.Members.AsNoTracking()
            .Select(m => new { m.JoinYear, m.MembershipKind, m.MembershipStatus, m.LocalStatusOverride }).ToListAsync(cancellationToken);
        var joined = members.Where(m => m.JoinYear is not null && m.MembershipKind != MembershipKind.Partner)
            .GroupBy(m => (int)m.JoinYear!.Value).ToDictionary(g => g.Key, g => g.Count());
        var left = await db.MemberExits.AsNoTracking().GroupBy(e => e.CarnivalYear).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var stats = await db.MemberYearStats.AsNoTracking().ToDictionaryAsync(s => s.CarnivalYear, cancellationToken);
        var liveTotal = members.Count(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active);

        var first = joined.Keys.Concat(stats.Keys).DefaultIfEmpty(current).Min();
        var rows = new List<MemberHistoryRow>();
        for (var year = first; year <= current; year++)
        {
            var stat = stats.GetValueOrDefault(year);
            int? computedLeft = year >= trackedSince ? left.GetValueOrDefault(year) : null;
            int? computedTotal = year == current ? liveTotal : stat?.SnapshotTotal;
            rows.Add(new MemberHistoryRow(
                year, stat?.ManualNew ?? joined.GetValueOrDefault(year), stat?.ManualLeft ?? computedLeft, stat?.ManualTotal ?? computedTotal,
                new MemberHistoryManual(stat?.ManualNew is not null, stat?.ManualLeft is not null, stat?.ManualTotal is not null)));
        }

        return new MemberHistoryReport(current, trackedSince, rows);
    }

    /// <summary>Cijfers van één carnavalsjaar invullen (leeg = weer berekenen).</summary>
    public async Task SetAsync(int year, MemberHistoryInput input, CancellationToken cancellationToken)
    {
        Validate(year, input);
        await UpsertAsync(year, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member-history.changed", "MemberYearStat", year.ToString(CultureInfo.InvariantCulture), null,
            $"{input.NewMembers}/{input.LeftMembers}/{input.TotalMembers}"), cancellationToken);
    }

    /// <summary>
    /// Import uit Excel (de API leest het bestand): per regel een carnavalsjaar ("2027" of "2026/2027") met aantallen; een
    /// lege waarde laat het bestaande cijfer staan. Geeft het aantal jaren.
    /// </summary>
    public async Task<int> ImportAsync(IReadOnlyList<MemberHistoryImportRow> rows, CancellationToken cancellationToken)
    {
        int? Number(MemberHistoryImportRow row, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0
                ? n
                : throw new DomainException(ErrorCodes.Validation, $"Regel {row.RowNumber}: \"{text.Trim()}\" is geen aantal.");
        }

        var count = 0;
        foreach (var row in rows)
        {
            // "2026/2027" of "2026-2027": het carnavalsjaar is het laatste jaar.
            var digits = row.Year.Split('/', '-', ' ').LastOrDefault(p => p.Trim().Length == 4)?.Trim();
            if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            {
                continue;
            }

            var input = new MemberHistoryInput(Number(row, row.NewMembers), Number(row, row.LeftMembers), Number(row, row.TotalMembers));
            Validate(year, input);
            await UpsertAsync(year, input, cancellationToken, keepExisting: true);
            count++;
        }

        if (count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Geen regels met een carnavalsjaar gevonden.");
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member-history.imported", "MemberYearStat", "excel", null, count.ToString(CultureInfo.InvariantCulture)), cancellationToken);
        return count;
    }

    private static void Validate(int year, MemberHistoryInput input)
    {
        if (year is < 1900 or > 2100 || input.NewMembers < 0 || input.LeftMembers < 0 || input.TotalMembers < 0)
        {
            throw new DomainException(ErrorCodes.Validation, $"Controleer de cijfers van {year}: alleen hele aantallen van 0 of meer.");
        }
    }

    private async Task UpsertAsync(int year, MemberHistoryInput input, CancellationToken cancellationToken, bool keepExisting = false)
    {
        var stat = db.MemberYearStats.Local.SingleOrDefault(s => s.CarnivalYear == year)
            ?? await db.MemberYearStats.SingleOrDefaultAsync(s => s.CarnivalYear == year, cancellationToken);
        if (stat is null)
        {
            stat = new MemberYearStat { CarnivalYear = year };
            db.MemberYearStats.Add(stat);
        }

        stat.ManualNew = keepExisting ? input.NewMembers ?? stat.ManualNew : input.NewMembers;
        stat.ManualLeft = keepExisting ? input.LeftMembers ?? stat.ManualLeft : input.LeftMembers;
        stat.ManualTotal = keepExisting ? input.TotalMembers ?? stat.ManualTotal : input.TotalMembers;
    }
}

/// <summary>Legt dagelijks uitschrijvingen en het totaal van het lopende carnavalsjaar vast.</summary>
public sealed class MemberHistoryJob(MemberHistory history) : IRecurringJob
{
    public const string JobName = "member-history";

    public Task ExecuteAsync(CancellationToken cancellationToken) => history.RecordAsync(cancellationToken);
}
