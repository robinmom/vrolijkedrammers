using Drammers.Infrastructure.Content;

namespace Drammers.UnitTests.Content;

/// <summary>Fase 21g: nieuws en foto's per carnavalsjaar, met een archief voor de oudere jaren.</summary>
public class SeasonCalendarTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    /// <summary>Twee aansluitende jaren in de backend, 2026/2027 actief.</summary>
    private static SeasonCalendar Calendar() => new(
    [
        ("2025/2026", new DateOnly(2025, 11, 11), new DateOnly(2026, 2, 18), false),
        ("2026/2027", new DateOnly(2026, 2, 19), new DateOnly(2027, 2, 10), true),
    ], Today);

    [Theory]
    [InlineData(2026, "2026-02-18")]
    [InlineData(2027, "2027-02-10")]
    [InlineData(2025, "2025-03-05")]
    [InlineData(2016, "2016-02-10")]
    public void Aswoensdag_is_46_dagen_voor_Pasen(int year, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), SeasonCalendar.AshWednesday(year));

    [Theory]
    [InlineData("2026-10-01", "2026-2027")]
    [InlineData("2027-06-01", "2026-2027")] // na het actieve jaar, nog geen nieuw jaar: blijft actueel
    [InlineData("2026-02-18", "2025-2026")]
    [InlineData("2025-11-10", "2025-2026")] // vóór 11-11 maar na Aswoensdag 2025: sluit aan op het oudste jaar
    [InlineData("2025-03-05", "2024-2025")] // Aswoensdag 2025: afgeleid seizoen
    [InlineData("2016-06-01", "2016-2017")]
    public void Een_datum_valt_in_precies_één_jaar(string date, string expected) =>
        Assert.Equal(expected, Calendar().Of(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)).Slug);

    [Fact]
    public void Het_actieve_jaar_loopt_door_en_het_archief_bevat_alleen_oudere_jaren()
    {
        var calendar = Calendar();
        Assert.Equal(("2026-2027", new DateOnly(2026, 2, 19), (DateOnly?)null), (calendar.Current.Slug, calendar.Current.Start, calendar.Current.End));

        var archive = calendar.Archive([
            new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2019, 2, 1, 10, 0, 0, DateTimeKind.Utc),
        ]);
        Assert.Equal(["2025-2026", "2018-2019"], archive.Select(s => s.Slug));
    }

    [Fact]
    public void Jaartal_uit_de_url_wordt_herkend()
    {
        var calendar = Calendar();
        Assert.Same(calendar.Current, calendar.Find("2026-2027"));
        Assert.Equal((new DateOnly(2025, 3, 6), new DateOnly(2026, 2, 18)), (calendar.Find("2025-2026")!.Start, calendar.Find("2025-2026")!.End!.Value));
        Assert.Equal((new DateOnly(2018, 2, 15), new DateOnly(2019, 3, 6)), (calendar.Find("2018-2019")!.Start, calendar.Find("2018-2019")!.End!.Value));
        Assert.Null(calendar.Find("2018-2020"));
        Assert.Null(calendar.Find("abc"));
        Assert.Null(calendar.Find("1900-1901"));
    }

    [Fact]
    public void Begin_van_het_seizoen_is_middernacht_in_Nederland()
    {
        var season = Calendar().Find("2025-2026")!;
        Assert.Equal(new DateTime(2025, 3, 5, 23, 0, 0, DateTimeKind.Utc), season.StartUtc);
        Assert.Equal(new DateTime(2026, 2, 18, 23, 0, 0, DateTimeKind.Utc), season.EndUtc);
    }

    [Fact]
    public void Zonder_jaren_in_de_backend_wordt_alles_afgeleid()
    {
        var calendar = new SeasonCalendar([], Today);
        Assert.Equal(("2026-2027", new DateOnly(2026, 2, 19)), (calendar.Current.Slug, calendar.Current.Start));
        Assert.Equal("2025-2026", calendar.Of(new DateOnly(2026, 1, 1)).Slug);
    }
}
