using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Categories;
using Drammers.Modules.Parade.Parades;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

public enum StartNumberMode
{
    /// <summary>Alleen groepen zonder startnummer krijgen het eerstvolgende vrije nummer; bestaande nummers blijven.</summary>
    FillEmpty,

    /// <summary>Alle ingedeelde groepen krijgen startAt, startAt + 1, … in de volgorde van de optocht.</summary>
    Renumber,
}

/// <summary>Kaart in het samenstelscherm (docs/13 §7.3).</summary>
public sealed record CompositionCard(
    Guid Id, int? RegistrationNumber, int? StartNumber, string? GroupName, string? CategoryName, bool Youth, bool HasVehicle, string? Subject,
    int Participants, decimal LengthMeters, bool LengthMeasured, string? AdditionalInformation, RegistrationStatus Status, int? ParadeOrder);

public sealed record Composition(
    int Version, IReadOnlyList<CompositionCard> Ordered, IReadOnlyList<CompositionCard> Unassigned, int Participants, decimal LengthMeters,
    decimal DefaultSpacingMeters, IReadOnlyList<CategoryTotals> Categories, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FixedEntries, int FirstStartNumber);

public sealed record StartNumberChange(Guid Id, string? GroupName, int? RegistrationNumber, int? OldStartNumber, int? NewStartNumber, bool Published);

public sealed record StartNumberPreview(int Version, IReadOnlyList<StartNumberChange> Changes, bool AffectsPublished);

/// <summary>
/// Optocht samenstellen (fase 12b, docs/13 §7.3): de commissie zet goedgekeurde groepen op volgorde. Slepen wijzigt
/// alleen <c>parade_order</c> (optimistic concurrency via <c>composition_version</c>); startnummers veranderen pas met
/// "Startnummers genereren" na een preview. Gepubliceerde nummers hernummeren vraagt de bevestiging "HERNUMMER".
/// </summary>
public sealed class ParadeComposition(DrammersDbContext db, ParadeLineup lineup, IAuditLogger audit)
{
    public const string RenumberConfirmation = "HERNUMMER";

    private async Task<Parade> CurrentParadeAsync(CancellationToken cancellationToken) =>
        await db.Parades.AsNoTracking().Where(p => db.CarnivalYears.Any(y => y.Id == p.CarnivalYearId && y.Active)).SingleOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is nog geen optocht voor het actieve carnavalsjaar.", DomainErrorKind.NotFound);

    private async Task<List<(ParadeRegistration Registration, ParadeCategory? Category)>> LineupAsync(Guid paradeId, CancellationToken cancellationToken)
    {
        var rows = await (
            from r in db.ParadeRegistrations.AsNoTracking()
            where r.ParadeId == paradeId && ParadeLineup.InLineup.Contains(r.Status)
            join c in db.ParadeCategories.AsNoTracking() on r.CategoryId equals c.Id into cs
            from c in cs.DefaultIfEmpty()
            select new { r, c }).ToListAsync(cancellationToken);
        return [.. rows.Select(x => (x.r, (ParadeCategory?)x.c))];
    }

    public async Task<Composition> GetAsync(CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var rows = await LineupAsync(parade.Id, cancellationToken);
        var cards = rows.Select(x => new CompositionCard(
            x.Registration.Id, x.Registration.RegistrationNumber, x.Registration.StartNumber, x.Registration.GroupName, x.Category?.Name,
            x.Category?.AgeGroup == AgeGroup.Youth, x.Category?.HasVehicle ?? false, x.Registration.Subject,
            x.Registration.ChildrenCount + x.Registration.AdultCount,
            x.Registration.MeasuredLengthMeters ?? x.Registration.EstimatedLengthMeters ?? 0m, x.Registration.MeasuredLengthMeters != null,
            x.Registration.AdditionalInformation, x.Registration.Status, x.Registration.ParadeOrder)).ToList();
        var ordered = cards.Where(c => c.ParadeOrder != null).OrderBy(c => c.ParadeOrder).ThenBy(c => c.RegistrationNumber).ToList();
        var unassigned = cards.Where(c => c.ParadeOrder == null).OrderBy(c => c.StartNumber ?? int.MaxValue).ThenBy(c => c.RegistrationNumber).ToList();
        var spacing = rows.Where(x => x.Registration.ParadeOrder != null).ToDictionary(x => x.Registration.Id, x => x.Registration.SpacingAfterMeters ?? parade.DefaultSpacingMeters);
        return new Composition(
            parade.CompositionVersion, ordered, unassigned,
            ordered.Sum(c => c.Participants),
            ordered.Sum(c => c.LengthMeters + spacing[c.Id]),
            parade.DefaultSpacingMeters,
            [.. ordered.GroupBy(c => c.CategoryName ?? "Zonder categorie").OrderBy(g => g.Key, StringComparer.CurrentCulture)
                .Select(g => new CategoryTotals(g.Key, g.Count(), g.Sum(c => c.Participants), g.Sum(c => c.LengthMeters)))],
            Warnings(ordered),
            [.. parade.FixedEntries.Select(e => e.Name)], parade.FirstGroupStartNumber);
    }

    /// <summary>Signalen voor de commissie, bijv. drie wagens of drie groepen uit dezelfde categorie achter elkaar.</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<CompositionCard> ordered)
    {
        var warnings = new List<string>();
        void Runs(Func<CompositionCard, string?> key, Func<string, int, int, int, string> text)
        {
            var start = 0;
            for (var i = 1; i <= ordered.Count; i++)
            {
                var k = key(ordered[start]);
                if (i < ordered.Count && k is not null && key(ordered[i]) == k)
                {
                    continue;
                }

                if (k is not null && i - start >= 3)
                {
                    warnings.Add(text(k, i - start, start + 1, i));
                }

                start = i;
            }
        }

        Runs(c => c.HasVehicle ? "voertuig" : null, (_, n, from, to) => $"{n} groepen met een voertuig achter elkaar (positie {from}–{to}).");
        Runs(c => c.CategoryName, (k, n, from, to) => $"{n} × {k} achter elkaar (positie {from}–{to}).");
        return warnings;
    }

    /// <summary>
    /// Slaat de volgorde op: de opgegeven groepen krijgen 1, 2, 3, …; de overige goedgekeurde groepen gaan naar "Niet
    /// ingedeeld". Is de versie intussen door een ander verhoogd, dan 412.
    /// </summary>
    public async Task<int> SaveOrderAsync(int version, IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        if (orderedIds.Distinct().Count() != orderedIds.Count)
        {
            throw new DomainException(ErrorCodes.Validation, "Een groep staat dubbel in de volgorde.");
        }

        var lineupIds = (await LineupAsync(parade.Id, cancellationToken)).Select(x => x.Registration.Id).ToHashSet();
        if (orderedIds.Any(id => !lineupIds.Contains(id)))
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen goedgekeurde inschrijvingen van deze optocht kunnen in de volgorde.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var bumped = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE parade.Parade SET composition_version = composition_version + 1 WHERE id = {parade.Id} AND composition_version = {version}", cancellationToken);
        if (bumped == 0)
        {
            throw Changed();
        }

        // Eén opdracht voor alle posities (geen veldhistorie per positie; de volgorde zegt de groep niets).
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE parade.ParadeRegistration SET parade_order = NULL WHERE parade_id = {parade.Id} AND parade_order IS NOT NULL", cancellationToken);
        if (orderedIds.Count > 0)
        {
            var values = string.Join(", ", orderedIds.Select((_, i) => $"({{{i * 2}}}, {{{(i * 2) + 1}}})"));
            var parameters = orderedIds.SelectMany((id, i) => new object[] { id, i + 1 }).ToArray();
#pragma warning disable EF1002 // Alleen parameterplaatsen worden samengesteld; de waarden gaan als parameters mee.
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE r SET r.parade_order = v.n FROM parade.ParadeRegistration r JOIN (VALUES {values}) AS v(id, n) ON r.id = v.id", parameters, cancellationToken);
#pragma warning restore EF1002
        }

        await transaction.CommitAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade.order-saved", "Parade", parade.Id.ToString(), null,
            JsonSerializer.Serialize(new { version = version + 1, ordered = orderedIds.Count })), cancellationToken);
        return version + 1;
    }

    public async Task<StartNumberPreview> PreviewAsync(StartNumberMode mode, int startAt, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var changes = await ChangesAsync(parade, mode, startAt, cancellationToken);
        return new StartNumberPreview(parade.CompositionVersion, [.. changes.Select(c => c.Change)], changes.Any(c => c.Change.Published));
    }

    /// <summary>Voert de preview uit, maar alleen als de volgorde sindsdien niet is gewijzigd (zelfde versie).</summary>
    public async Task<int> ApplyAsync(int version, StartNumberMode mode, int startAt, string? confirmation, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        if (parade.CompositionVersion != version)
        {
            throw Changed();
        }

        var changes = await ChangesAsync(parade, mode, startAt, cancellationToken);
        if (changes.Any(c => c.Change.Published) && !string.Equals(confirmation?.Trim(), RenumberConfirmation, StringComparison.Ordinal))
        {
            throw new DomainException(ErrorCodes.Validation, $"Er veranderen gepubliceerde startnummers; de groepen krijgen daarvan bericht. Typ {RenumberConfirmation} om te bevestigen.");
        }

        if (changes.Count > 0)
        {
            await lineup.ApplyStartNumbersAsync([.. changes.Select(c => (c.Registration, c.Change.NewStartNumber))], cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("parade.start-numbers-generated", "Parade", parade.Id.ToString(), null,
            JsonSerializer.Serialize(new
            {
                mode = mode.ToString(),
                startAt,
                changes = changes.Select(c => new { id = c.Registration.Id, from = c.Change.OldStartNumber, to = c.Change.NewStartNumber }),
            })), cancellationToken);
        return changes.Count;
    }

    private async Task<List<(ParadeRegistration Registration, StartNumberChange Change)>> ChangesAsync(
        Parade parade, StartNumberMode mode, int startAt, CancellationToken cancellationToken)
    {
        if (startAt is < 1 or > 9999)
        {
            throw new DomainException(ErrorCodes.Validation, "Het eerste startnummer is een getal van 1 tot en met 9999.");
        }

        if (startAt < parade.FirstGroupStartNumber)
        {
            throw new DomainException(ErrorCodes.Validation,
                $"Startnummer 1 tot en met {parade.FixedEntries.Count} zijn van de vaste plekken; begin bij {parade.FirstGroupStartNumber} of later.");
        }

        var rows = (await LineupAsync(parade.Id, cancellationToken)).Select(x => x.Registration).ToList();
        var ordered = rows.Where(r => r.ParadeOrder != null).OrderBy(r => r.ParadeOrder).ToList();
        if (ordered.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Zet eerst groepen op volgorde; daarna kun je startnummers genereren.");
        }

        var target = new Dictionary<Guid, int?>();
        if (mode == StartNumberMode.FillEmpty)
        {
            var used = (await db.ParadeRegistrations.AsNoTracking().Where(r => r.ParadeId == parade.Id && r.StartNumber != null)
                .Select(r => r.StartNumber!.Value).ToListAsync(cancellationToken)).ToHashSet();
            var next = startAt;
            foreach (var registration in ordered.Where(r => r.StartNumber == null))
            {
                while (used.Contains(next))
                {
                    next++;
                }

                target[registration.Id] = next;
                used.Add(next);
            }
        }
        else
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                target[ordered[i].Id] = startAt + i;
            }

            // Niet-ingedeelde groepen waarvan het nummer nu door een ingedeelde groep wordt gebruikt, verliezen het.
            var taken = target.Values.ToHashSet();
            foreach (var registration in rows.Where(r => r.ParadeOrder == null && r.StartNumber is { } n && taken.Contains(n)))
            {
                target[registration.Id] = null;
            }
        }

        var byId = rows.ToDictionary(r => r.Id);
        var changes = target.Where(t => byId[t.Key].StartNumber != t.Value)
            .Select(t => (byId[t.Key], new StartNumberChange(t.Key, byId[t.Key].GroupName, byId[t.Key].RegistrationNumber, byId[t.Key].StartNumber, t.Value,
                byId[t.Key].Status == RegistrationStatus.StartNumberAssigned)))
            .OrderBy(c => c.Item2.NewStartNumber ?? int.MaxValue).ToList();
        if (changes.Any(c => c.Item1.Status == RegistrationStatus.Final))
        {
            throw new DomainException(ErrorCodes.InvalidStatusTransition, "De optocht is vastgesteld; startnummers van vastgestelde groepen wijzigen kan hier niet.", DomainErrorKind.Conflict);
        }

        return changes;
    }

    private static DomainException Changed() =>
        new(ErrorCodes.RegistrationChanged, "De volgorde is intussen door iemand anders gewijzigd. Laad de pagina opnieuw.", DomainErrorKind.PreconditionFailed);
}
