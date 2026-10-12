using Drammers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

/// <summary>
/// Inschrijfjaar van een nieuw lid (2026-10-12): het eerste carnaval dat het lid meeviert. Wie zich na de laatste
/// carnavalsdag inschrijft (carnaval 2026: 15–17 februari), telt vanaf het volgende jaar (2027). De datums komen uit de
/// carnavalsjaren in het portal; staat het jaar daar (nog) niet in, dan rekent de API met carnavalsdinsdag = Pasen − 47 dagen.
/// </summary>
public static class JoinYears
{
    public static async Task<short> ForDateAsync(DrammersDbContext db, DateOnly date, CancellationToken cancellationToken)
    {
        var configured = await db.CarnivalYears.AsNoTracking()
            .Where(y => y.CarnivalEndDate >= date)
            .OrderBy(y => y.CarnivalEndDate)
            .Select(y => (DateOnly?)y.CarnivalEndDate)
            .FirstOrDefaultAsync(cancellationToken);

        // Alleen een carnavalsjaar dat bij dit carnaval hoort (niet een ver toekomstig jaar terwijl het eerstvolgende ontbreekt).
        var calculated = ForDate(date);
        return configured is { } end && end.Year <= calculated ? (short)end.Year : calculated;
    }

    /// <summary>Zonder carnavalsjaren: het jaar van de eerstvolgende carnavalsdinsdag (inclusief vandaag).</summary>
    public static short ForDate(DateOnly date) =>
        (short)(date <= CarnivalTuesday(date.Year) ? date.Year : date.Year + 1);

    /// <summary>Carnavalsdinsdag (dag vóór Aswoensdag) = Pasen − 47 dagen.</summary>
    public static DateOnly CarnivalTuesday(int year) => Easter(year).AddDays(-47);

    /// <summary>Paaszondag (Gregoriaans, anonieme algoritme van Meeus/Jones/Butcher).</summary>
    public static DateOnly Easter(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
