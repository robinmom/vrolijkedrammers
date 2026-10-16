using System.Globalization;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Festival;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Festival;

/// <summary>Een dans in het programma; <c>Time</c> is de starttijd (HH:mm), <c>Number</c> de plek in de categorie.</summary>
public sealed record ProgramDance(Guid EntryId, Guid RegistrationId, int Number, string Time, string Club, string Names, string? Title, int DancerCount, decimal AverageAge);

/// <summary>Een onderdeel; <c>StartOverridden</c>: de starttijd is met de hand gezet (anders sluit het aan op het vorige).</summary>
public sealed record ProgramItemView(
    Guid Id, FestivalProgramItemKind Kind, string Title, string? CategoryKey, string Start, string End, bool StartOverridden, int Minutes,
    bool Solo, IReadOnlyList<ProgramDance> Dances);

public sealed record ProgramView(Guid FestivalId, string FestivalName, DateOnly FestivalDate, string Location, string Start, string End, DateTime? DrawnAt,
    int Categories, int Dances, IReadOnlyList<ProgramItemView> Items);

/// <summary>
/// Het programma van het Dansgardefestival (2026-10-15): de goedgekeurde dansen per programmacategorie, met blokken
/// (opening, pauze, prijsuitreiking). Per categorie een starttijd (leeg = aansluiten op het vorige) en minuten per dans;
/// categorieën en dansen zijn te verschuiven en de volgorde binnen elke categorie is te loten.
/// </summary>
public sealed class FestivalProgramService(DrammersDbContext db, IClock clock, IAuditLogger audit)
{
    public const int DefaultMinutesPerDance = 4;

    public const int DefaultSoloMinutes = 5;

    /// <summary>Het programma; nieuwe categorieën (pas goedgekeurde dansen) komen op hun standaardplek erbij, lege verdwijnen.</summary>
    public async Task<ProgramView> GetAsync(CancellationToken cancellationToken)
    {
        var festival = await ActiveFestivalAsync(cancellationToken);
        var (items, entries) = await SyncAsync(festival, cancellationToken);
        return Build(festival, items, entries);
    }

    public async Task SetStartAsync(TimeOnly start, CancellationToken cancellationToken)
    {
        var festival = await ActiveFestivalAsync(cancellationToken);
        festival.ProgramStart = start;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> AddBlockAsync(string title, int minutes, Guid? afterItemId, CancellationToken cancellationToken)
    {
        var festival = await ActiveFestivalAsync(cancellationToken);
        var (items, _) = await SyncAsync(festival, cancellationToken);
        var clean = CleanTitle(title);
        ValidateMinutes(minutes);
        var index = afterItemId is { } after ? items.FindIndex(i => i.Id == after) + 1 : items.Count;
        if (index <= 0 && afterItemId is not null)
        {
            throw new DomainException(ErrorCodes.NotFound, "Onderdeel niet gevonden.", DomainErrorKind.NotFound);
        }

        var block = new DanceFestivalProgramItem { Id = IdGenerator.NewId(), FestivalId = festival.Id, Kind = FestivalProgramItemKind.Block, Title = clean, Minutes = minutes };
        db.DanceFestivalProgramItems.Add(block);
        items.Insert(index, block);
        Renumber(items);
        await db.SaveChangesAsync(cancellationToken);
        return block.Id;
    }

    /// <summary>Starttijd (leeg = aansluiten), minuten en bij een blok de titel.</summary>
    public async Task UpdateItemAsync(Guid id, TimeOnly? start, int minutes, string? title, CancellationToken cancellationToken)
    {
        var item = await ItemAsync(id, cancellationToken);
        ValidateMinutes(minutes);
        if (item.Kind == FestivalProgramItemKind.Category && minutes < 1)
        {
            throw new DomainException(ErrorCodes.Validation, "Geef minstens 1 minuut per dans.");
        }

        item.StartTime = start;
        item.Minutes = minutes;
        if (item.Kind == FestivalProgramItemKind.Block && title is not null)
        {
            item.Title = CleanTitle(title);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteBlockAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await ItemAsync(id, cancellationToken);
        if (item.Kind != FestivalProgramItemKind.Block)
        {
            throw new DomainException(ErrorCodes.Validation, "Een categorie verdwijnt vanzelf als er geen goedgekeurde dansen meer in zitten.", DomainErrorKind.Conflict);
        }

        db.DanceFestivalProgramItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Een onderdeel één plek omhoog (-1) of omlaag (+1).</summary>
    public async Task MoveItemAsync(Guid id, int direction, CancellationToken cancellationToken)
    {
        var festival = await ActiveFestivalAsync(cancellationToken);
        var (items, _) = await SyncAsync(festival, cancellationToken);
        var index = items.FindIndex(i => i.Id == id);
        if (index < 0)
        {
            throw new DomainException(ErrorCodes.NotFound, "Onderdeel niet gevonden.", DomainErrorKind.NotFound);
        }

        var target = index + Math.Sign(direction);
        if (target >= 0 && target < items.Count)
        {
            (items[index], items[target]) = (items[target], items[index]);
            Renumber(items);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Een dans één plek omhoog (-1) of omlaag (+1) binnen zijn categorie.</summary>
    public async Task MoveDanceAsync(Guid entryId, int direction, CancellationToken cancellationToken)
    {
        var festival = await ActiveFestivalAsync(cancellationToken);
        var (_, entries) = await SyncAsync(festival, cancellationToken);
        var entry = entries.SingleOrDefault(e => e.Entry.Id == entryId)
            ?? throw new DomainException(ErrorCodes.NotFound, "Dans niet gevonden in het programma.", DomainErrorKind.NotFound);
        var group = entries.Where(e => e.Key == entry.Key).Select(e => e.Entry).ToList();
        var index = group.IndexOf(entry.Entry);
        var target = index + Math.Sign(direction);
        if (target >= 0 && target < group.Count)
        {
            (group[index], group[target]) = (group[target], group[index]);
            for (var i = 0; i < group.Count; i++)
            {
                group[i].ProgramOrder = i + 1;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Loting: de volgorde binnen elke categorie willekeurig.</summary>
    public async Task DrawAsync(CancellationToken cancellationToken)
    {
        var festival = await ActiveFestivalAsync(cancellationToken);
        var (_, entries) = await SyncAsync(festival, cancellationToken);
        foreach (var group in entries.GroupBy(e => e.Key))
        {
            var order = group.Select(e => e.Entry).OrderBy(_ => Random.Shared.Next()).ToList();
            for (var i = 0; i < order.Count; i++)
            {
                order[i].ProgramOrder = i + 1;
            }
        }

        festival.DrawnAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("festival.program.drawn", "DanceFestival", festival.Id.ToString(), null, entries.Count.ToString(CultureInfo.InvariantCulture)), cancellationToken);
    }

    /// <summary>De programmacategorieën met hun dansen in programmavolgorde (voor de jury en de uitslag).</summary>
    public async Task<IReadOnlyList<ProgramItemView>> CategoriesAsync(Guid festivalId, CancellationToken cancellationToken)
    {
        var festival = await db.DanceFestivals.SingleAsync(f => f.Id == festivalId, cancellationToken);
        var (items, entries) = await SyncAsync(festival, cancellationToken);
        return [.. Build(festival, items, entries).Items.Where(i => i.Kind == FestivalProgramItemKind.Category)];
    }

    internal async Task<DanceFestival> ActiveFestivalAsync(CancellationToken cancellationToken) =>
        await db.DanceFestivals.Where(f => f.Active).OrderByDescending(f => f.FestivalDate).FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Er is nog geen Dansgardefestival. Maak het eerst aan bij Instellingen.", DomainErrorKind.NotFound);

    private sealed record ProgramEntry(DanceFestivalEntry Entry, DanceFestivalRegistration Registration, string Key);

    /// <summary>Goedgekeurde dansen (ook gewijzigd met behoud van plek) en de onderdelen; voegt ontbrekende categorieën toe.</summary>
    private async Task<(List<DanceFestivalProgramItem> Items, List<ProgramEntry> Entries)> SyncAsync(DanceFestival festival, CancellationToken cancellationToken)
    {
        var registrations = await db.DanceFestivalRegistrations.Include(r => r.Entries)
            .Where(r => r.FestivalId == festival.Id).ToListAsync(cancellationToken);
        var entries = registrations
            .SelectMany(r => r.Entries.Where(e => e.RemovedAt is null && (e.Status == DanceEntryStatus.Approved || e.HoldsPlace))
                .Select(e => new ProgramEntry(e, r, FestivalCategories.KeyOf(e))))
            .OrderBy(e => e.Entry.ProgramOrder ?? int.MaxValue).ThenBy(e => e.Registration.Number).ThenBy(e => e.Entry.SortOrder).ToList();
        var items = await db.DanceFestivalProgramItems.Where(i => i.FestivalId == festival.Id).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var changed = false;
        if (items.Count == 0)
        {
            items.Add(new DanceFestivalProgramItem { Id = IdGenerator.NewId(), FestivalId = festival.Id, Kind = FestivalProgramItemKind.Block, Title = "Opening", Minutes = 5 });
            items.Add(new DanceFestivalProgramItem { Id = IdGenerator.NewId(), FestivalId = festival.Id, Kind = FestivalProgramItemKind.Block, Title = "Prijsuitreiking", Minutes = 30 });
            db.DanceFestivalProgramItems.AddRange(items);
            changed = true;
        }

        var keys = entries.Select(e => e.Key).Distinct().ToHashSet(StringComparer.Ordinal);
        foreach (var empty in items.Where(i => i.Kind == FestivalProgramItemKind.Category && !keys.Contains(i.CategoryKey!)).ToList())
        {
            db.DanceFestivalProgramItems.Remove(empty);
            items.Remove(empty);
            changed = true;
        }

        foreach (var key in keys.Where(k => items.All(i => i.CategoryKey != k)).OrderBy(FestivalCategories.DefaultRank))
        {
            // Nieuwe categorie op haar standaardplek: na de laatste categorie met een lagere rang, anders vóór de eerste
            // categorie (na de opening); nooit na de prijsuitreiking.
            var rank = FestivalCategories.DefaultRank(key);
            var index = items.FindLastIndex(i => i.Kind == FestivalProgramItemKind.Category && FestivalCategories.DefaultRank(i.CategoryKey!) < rank) + 1;
            if (index == 0)
            {
                var first = items.FindIndex(i => i.Kind == FestivalProgramItemKind.Category);
                index = first >= 0 ? first : items.Count > 0 && items[0].Title == "Opening" ? 1 : 0;
            }

            var award = items.FindIndex(i => i.Kind == FestivalProgramItemKind.Block && i.Title == "Prijsuitreiking");
            if (award >= 0 && index > award)
            {
                index = award;
            }

            var item = new DanceFestivalProgramItem
            {
                Id = IdGenerator.NewId(),
                FestivalId = festival.Id,
                Kind = FestivalProgramItemKind.Category,
                CategoryKey = key,
                Minutes = FestivalCategories.IsSolo(key) ? DefaultSoloMinutes : DefaultMinutesPerDance,
            };
            db.DanceFestivalProgramItems.Add(item);
            items.Insert(index, item);
            changed = true;
        }

        if (changed)
        {
            Renumber(items);
            await db.SaveChangesAsync(cancellationToken);
        }

        return (items, entries);
    }

    private static ProgramView Build(DanceFestival festival, List<DanceFestivalProgramItem> items, List<ProgramEntry> entries)
    {
        var cursor = festival.ProgramStart;
        var views = new List<ProgramItemView>();
        foreach (var item in items)
        {
            var start = item.StartTime ?? cursor;
            if (item.Kind == FestivalProgramItemKind.Block)
            {
                var end = start.AddMinutes(item.Minutes);
                views.Add(new ProgramItemView(item.Id, item.Kind, item.Title ?? "", null, Hm(start), Hm(end), item.StartTime is not null, item.Minutes, false, []));
                cursor = end;
                continue;
            }

            var dances = entries.Where(e => e.Key == item.CategoryKey).Select((e, i) => new ProgramDance(
                e.Entry.Id, e.Registration.Id, i + 1, Hm(start.AddMinutes(i * item.Minutes)), e.Registration.ClubName, e.Entry.Names, e.Entry.Title,
                e.Entry.DancerCount, e.Entry.AverageAge)).ToList();
            var finish = start.AddMinutes(dances.Count * item.Minutes);
            views.Add(new ProgramItemView(item.Id, item.Kind, FestivalCategories.NameOf(item.CategoryKey!), item.CategoryKey, Hm(start), Hm(finish),
                item.StartTime is not null, item.Minutes, FestivalCategories.IsSolo(item.CategoryKey!), dances));
            cursor = finish;
        }

        return new ProgramView(festival.Id, festival.Name, festival.FestivalDate, festival.Location, Hm(festival.ProgramStart), Hm(cursor), festival.DrawnAt,
            views.Count(v => v.Kind == FestivalProgramItemKind.Category), entries.Count, views);
    }

    private async Task<DanceFestivalProgramItem> ItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var festival = await ActiveFestivalAsync(cancellationToken);
        return await db.DanceFestivalProgramItems.SingleOrDefaultAsync(i => i.Id == id && i.FestivalId == festival.Id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Onderdeel niet gevonden.", DomainErrorKind.NotFound);
    }

    private static void Renumber(List<DanceFestivalProgramItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            items[i].SortOrder = (i + 1) * 10;
        }
    }

    private static string CleanTitle(string title)
    {
        var clean = title.Trim();
        return clean.Length is > 0 and <= 80 ? clean : throw new DomainException(ErrorCodes.Validation, "Geef het blok een naam (maximaal 80 tekens).");
    }

    private static void ValidateMinutes(int minutes)
    {
        if (minutes is < 0 or > 240)
        {
            throw new DomainException(ErrorCodes.Validation, "Minuten: tussen 0 en 240.");
        }
    }

    public static string Hm(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
