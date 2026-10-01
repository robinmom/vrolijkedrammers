using System.Globalization;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Content;

/// <summary>Een seizoen voor het nieuws- en fotoarchief, bijvoorbeeld <c>2025-2026</c> (fase 21g).</summary>
/// <param name="Slug">Jaartal zoals in de knop en de url: <c>2025-2026</c>.</param>
/// <param name="Start">Eerste dag (Nederlandse tijd).</param>
/// <param name="End">Laatste dag; <c>null</c> voor het actieve seizoen, dat doorloopt zolang er nog geen volgend jaar is.</param>
public sealed record CarnivalSeason(string Slug, DateOnly Start, DateOnly? End)
{
    /// <summary>Begin van het seizoen als UTC-tijdstip, voor vergelijken met publicatiedatums.</summary>
    public DateTime StartUtc => SeasonCalendar.ToUtc(Start);

    /// <summary>Eerste moment ná het seizoen (UTC); <c>null</c> als het seizoen doorloopt.</summary>
    public DateTime? EndUtc => End is { } end ? SeasonCalendar.ToUtc(end.AddDays(1)) : null;
}

/// <summary>
/// Verdeelt nieuws en foto's over carnavalsjaren (fase 21g). Het actieve carnavalsjaar is "actueel"; alles wat daarvóór
/// valt, staat in het archief onder het jaar waar de datum in valt. Voor datums van vóór het oudste carnavalsjaar in de
/// backend (overgezette berichten van de oude site) wordt het seizoen afgeleid: van de dag na Aswoensdag tot en met de
/// volgende Aswoensdag. Wat ná het actieve jaar valt, hoort bij het actieve jaar (zolang er nog geen nieuw jaar is).
/// </summary>
public sealed class SeasonCalendar
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly IReadOnlyList<CarnivalSeason> _years;

    public SeasonCalendar(IEnumerable<(string Name, DateOnly Start, DateOnly End, bool Active)> years, DateOnly today)
    {
        var ordered = years.OrderBy(y => y.Start).ToList();
        _years = [.. ordered.Select((y, i) => new CarnivalSeason(SlugOf(y.Name), i == 0 ? ConnectedStart(y.Name, y.Start) : y.Start, y.End))];
        var active = ordered.FindIndex(y => y.Active);
        var current = active >= 0 ? _years[active] : _years.LastOrDefault(y => y.Start <= today) ?? DerivedSeason(today);
        Current = current with { End = null };
    }

    /// <summary>Het actieve seizoen (open einde): alles vanaf de begindatum is actueel.</summary>
    public CarnivalSeason Current { get; }

    /// <summary>Het seizoen van een datum; het actieve seizoen voor alles vanaf de begindatum daarvan.</summary>
    public CarnivalSeason Of(DateOnly date)
    {
        if (date >= Current.Start)
        {
            return Current;
        }

        var year = _years.LastOrDefault(y => y.Start <= date && date <= y.End);
        if (year is not null)
        {
            return year;
        }

        // Vóór het oudste jaar in de backend: afgeleid seizoen, dat aansluit op het oudste jaar.
        var oldest = _years.FirstOrDefault();
        var derived = DerivedSeason(date);
        if (oldest is not null && date < oldest.Start)
        {
            return derived.Slug == oldest.Slug ? oldest : derived with { End = Min(derived.End!.Value, oldest.Start.AddDays(-1)) };
        }

        return derived;
    }

    public CarnivalSeason Of(DateTime utc) => Of(ToLocalDate(utc));

    /// <summary>Het seizoen bij een jaartal uit de url (<c>2025-2026</c>); <c>null</c> bij een onbekend formaat.</summary>
    public CarnivalSeason? Find(string? slug)
    {
        if (slug is null || slug.Length != 9 || slug[4] != '-'
            || !int.TryParse(slug.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var first)
            || !int.TryParse(slug.AsSpan(5, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var second)
            || second != first + 1 || first < 1958)
        {
            return null;
        }

        if (slug == Current.Slug)
        {
            return Current;
        }

        // De laatste dag van een afgeleid seizoen is Aswoensdag van het tweede jaar.
        var season = _years.FirstOrDefault(y => y.Slug == slug) ?? Of(AshWednesday(second));
        return season.Slug == slug ? season : null;
    }

    /// <summary>De archiefseizoenen (ouder dan het actieve) van een reeks datums, nieuwste eerst.</summary>
    public IReadOnlyList<CarnivalSeason> Archive(IEnumerable<DateTime> datesUtc) =>
        [.. datesUtc.Select(Of).Where(s => s.Slug != Current.Slug).DistinctBy(s => s.Slug).OrderByDescending(s => s.Start)];

    /// <summary><c>2025/2026</c> → <c>2025-2026</c>.</summary>
    public static string SlugOf(string name) => name.Replace('/', '-');

    public static DateTime ToUtc(DateOnly localDate) =>
        TimeZoneInfo.ConvertTimeToUtc(localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Amsterdam);

    public static DateOnly ToLocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Amsterdam));

    /// <summary>Aswoensdag: 46 dagen vóór Pasen (Gregoriaanse paasberekening).</summary>
    public static DateOnly AshWednesday(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = ((19 * a) + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        int m = (a + (11 * h) + (22 * l)) / 451, month = (h + l - (7 * m) + 114) / 31, day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day).AddDays(-46);
    }

    /// <summary>
    /// Het oudste jaar sluit aan op het afgeleide seizoen ervóór: het begint uiterlijk de dag na Aswoensdag van zijn
    /// eerste jaartal (2026/2027 begint dus uiterlijk op 19 februari 2026, ook als in de backend 11 november staat).
    /// </summary>
    private static DateOnly ConnectedStart(string name, DateOnly start) =>
        name.Length >= 4 && int.TryParse(name.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var first) && first is > 1900 and < 3000
            ? Min(start, AshWednesday(first).AddDays(1))
            : start;

    /// <summary>Afgeleid seizoen: van de dag na Aswoensdag tot en met de volgende Aswoensdag.</summary>
    private static CarnivalSeason DerivedSeason(DateOnly date)
    {
        var first = date > AshWednesday(date.Year) ? date.Year : date.Year - 1;
        return new CarnivalSeason($"{first}-{first + 1}", AshWednesday(first).AddDays(1), AshWednesday(first + 1));
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}

/// <summary>Laadt de carnavalsjaren uit de database als <see cref="SeasonCalendar"/>.</summary>
public sealed class CarnivalSeasons(DrammersDbContext db, IClock clock)
{
    public async Task<SeasonCalendar> LoadAsync(CancellationToken cancellationToken)
    {
        var years = await db.CarnivalYears.AsNoTracking().Select(y => new { y.Name, y.StartDate, y.EndDate, y.Active }).ToListAsync(cancellationToken);
        return new SeasonCalendar(years.Select(y => (y.Name, y.StartDate, y.EndDate, y.Active)), SeasonCalendar.ToLocalDate(clock.UtcNow.UtcDateTime));
    }
}

/// <summary>Filters op seizoen: nieuws op publicatiedatum, albums op albumdatum (anders publicatie- of aanmaakdatum).</summary>
public static class SeasonFilters
{
    public static IQueryable<Modules.Content.News.NewsItem> InSeason(this IQueryable<Modules.Content.News.NewsItem> query, CarnivalSeason season)
    {
        var (start, end) = (season.StartUtc, season.EndUtc);
        return end is { } e
            ? query.Where(n => (n.PublishAt ?? n.CreatedAt) >= start && (n.PublishAt ?? n.CreatedAt) < e)
            : query.Where(n => (n.PublishAt ?? n.CreatedAt) >= start);
    }

    public static IQueryable<Modules.Content.Photos.PhotoAlbum> InSeason(this IQueryable<Modules.Content.Photos.PhotoAlbum> query, CarnivalSeason season)
    {
        var (start, startUtc, end, endUtc) = (season.Start, season.StartUtc, season.End, season.EndUtc);
        return end is { } e
            ? query.Where(a => a.AlbumDate != null
                ? a.AlbumDate >= start && a.AlbumDate <= e
                : (a.PublishAt ?? a.CreatedAt) >= startUtc && (a.PublishAt ?? a.CreatedAt) < endUtc)
            : query.Where(a => a.AlbumDate != null ? a.AlbumDate >= start : (a.PublishAt ?? a.CreatedAt) >= startUtc);
    }

    /// <summary>De datum waarop een album in een seizoen valt (als UTC-tijdstip, voor <see cref="SeasonCalendar.Archive"/>).</summary>
    public static DateTime AlbumMoment(DateOnly? albumDate, DateTime publishedOrCreated) =>
        albumDate is { } d ? SeasonCalendar.ToUtc(d) : publishedOrCreated;
}
