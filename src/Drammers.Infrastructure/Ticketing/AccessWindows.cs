using System.Globalization;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Content.Events;
using Drammers.Modules.Content.Shared;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Ticketing;

/// <summary>
/// Toegangsmoment: een activiteit met toegangscontrole (<see cref="EventId"/>) of anders een carnavalsdag
/// (<see cref="CarnivalDay"/>). <see cref="Key"/> is de sleutel in het portal ("dag-2027-02-13" of de event-id).
/// </summary>
public sealed record AccessEvent(string Key, Guid? EventId, DateOnly? CarnivalDay, string Title, DateTime StartAt, DateTime? EndAt);

/// <summary>
/// Wanneer de QR geldig is en er gescand kan worden — voor beide dezelfde regels (besluit product owner 2026-09-29):
/// tijdens de carnavalsperiode van het actieve carnavalsjaar én tijdens elke activiteit met toegangscontrole (ook
/// buiten carnaval). Loopt een activiteit met toegangscontrole, dan horen de scans bij die activiteit; anders bij de
/// carnavalsdag. Een carnavalsdag loopt tot 06:00, zodat de nacht na middernacht bij de avond ervoor hoort.
/// </summary>
public sealed class AccessWindows(DrammersDbContext db, IClock clock)
{
    /// <summary>De scanner staat al zo lang vóór de begintijd open (opstellen, vroege gasten).</summary>
    public static readonly TimeSpan OpensBefore = TimeSpan.FromHours(2);

    /// <summary>Zonder eindtijd duurt een activiteit voor de toegangscontrole zo lang.</summary>
    public static readonly TimeSpan DefaultLength = TimeSpan.FromHours(8);

    /// <summary>Tot dit tijdstip hoort de nacht bij de dag ervoor.</summary>
    public static readonly TimeSpan DayBoundary = TimeSpan.FromHours(6);

    private static readonly TimeZoneInfo Loil = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    private DateTime Now => clock.UtcNow.UtcDateTime;

    private IQueryable<Event> ControlledEvents => db.Events.AsNoTracking().Where(e => e.AccessControl && e.Status != PublicationStatus.Draft);

    public static AccessEvent ForEvent(Event e) => new(e.Id.ToString(), e.Id, null, e.Title, e.StartAt, e.EndAt);

    public static string DayKey(DateOnly day) => $"dag-{day:yyyy-MM-dd}";

    /// <summary>Carnavalsdag als toegangsmoment: van 00:00 tot de volgende ochtend 06:00 (Loil).</summary>
    public static AccessEvent ForCarnivalDay(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        var end = day.AddDays(1).ToDateTime(TimeOnly.FromTimeSpan(DayBoundary));
        return new AccessEvent(DayKey(day), null, day, $"Carnaval · {start.ToString("dddd d MMMM", Dutch)}",
            TimeZoneInfo.ConvertTimeToUtc(start, Loil), TimeZoneInfo.ConvertTimeToUtc(end, Loil));
    }

    public static bool TryParseDayKey(string key, out DateOnly day) =>
        DateOnly.TryParseExact(key.StartsWith("dag-", StringComparison.Ordinal) ? key[4..] : "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>Het venster van een activiteit: <see cref="OpensBefore"/> vóór de begintijd tot de eindtijd.</summary>
    public static (DateTime From, DateTime To) Window(AccessEvent e) =>
        e.EventId is null ? (e.StartAt, e.EndAt!.Value) : (e.StartAt - OpensBefore, e.EndAt ?? e.StartAt + DefaultLength);

    public async Task<CarnivalYear?> ActiveYearAsync(CancellationToken cancellationToken) =>
        await db.CarnivalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Active, cancellationToken);

    /// <summary>De lopende activiteit met toegangscontrole, of anders de carnavalsdag; <c>null</c> buiten beide.</summary>
    public async Task<AccessEvent?> CurrentAsync(CancellationToken cancellationToken)
    {
        var from = Now + OpensBefore;
        var events = await ControlledEvents.Where(e => e.StartAt <= from && e.StartAt >= Now - TimeSpan.FromDays(2))
            .OrderByDescending(e => e.StartAt).ToListAsync(cancellationToken);
        var running = events.Select(ForEvent).FirstOrDefault(e => Now <= Window(e).To);
        if (running is not null)
        {
            return running;
        }

        var year = await ActiveYearAsync(cancellationToken);
        if (year is null)
        {
            return null;
        }

        var (carnivalFrom, carnivalTo) = MemberTickets.Validity(year);
        var now = clock.UtcNow;
        if (now < carnivalFrom || now > carnivalTo)
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(Now, Loil) - DayBoundary;
        var day = DateOnly.FromDateTime(local);
        return ForCarnivalDay(day < year.CarnivalStartDate ? year.CarnivalStartDate : day > year.CarnivalEndDate ? year.CarnivalEndDate : day);
    }

    /// <summary>Het eerstvolgende toegangsmoment: een activiteit met toegangscontrole of de start van carnaval.</summary>
    public async Task<AccessEvent?> NextAsync(CancellationToken cancellationToken)
    {
        var next = await ControlledEvents.Where(e => e.StartAt > Now).OrderBy(e => e.StartAt).FirstOrDefaultAsync(cancellationToken);
        var year = await ActiveYearAsync(cancellationToken);
        var carnival = year is not null && MemberTickets.Validity(year).From > clock.UtcNow ? ForCarnivalDay(year.CarnivalStartDate) : null;
        return (next, carnival) switch
        {
            (null, null) => null,
            (null, { } c) => c,
            ({ } e, null) => ForEvent(e),
            ({ } e, { } c) => e.StartAt < c.StartAt ? ForEvent(e) : c,
        };
    }
}
