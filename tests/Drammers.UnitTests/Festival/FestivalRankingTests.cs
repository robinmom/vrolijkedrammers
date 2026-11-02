using Drammers.Infrastructure.Festival;
using Drammers.Modules.Content.Festival;

namespace Drammers.UnitTests.Festival;

/// <summary>Uitslag van het Dansgardefestival: hoogste totaal; gelijk → hoogste uitvoering; nog gelijk → gedeelde plaats.</summary>
public class FestivalRankingTests
{
    private static ResultRow Row(string names, decimal total, decimal execution) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 0, "Club", names, null, 0, 0, execution, 0, total, []);

    [Fact]
    public void Gelijk_totaal_hoogste_uitvoering_wint_anders_gedeelde_plaats()
    {
        var ranked = FestivalJudgingService.Rank([Row("A", 60, 15), Row("B", 62, 16), Row("C", 62, 17), Row("D", 60, 15), Row("E", 50, 20)]);
        Assert.Equal([("C", 1), ("B", 2), ("A", 3), ("D", 3), ("E", 5)], ranked.Select(r => (r.Names, r.Place)).ToArray());
    }

    /// <summary>Halve punten (2026-11-02): een half punt verschil in het totaal beslist de plaats.</summary>
    [Fact]
    public void Halve_punten_beslissen_de_plaats()
    {
        var ranked = FestivalJudgingService.Rank([Row("A", 60m, 15m), Row("B", 60.5m, 15m), Row("C", 60m, 15.5m)]);
        Assert.Equal([("B", 1), ("C", 2), ("A", 3)], ranked.Select(r => (r.Names, r.Place)).ToArray());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7.5, true)]
    [InlineData(10, true)]
    [InlineData(7.25, false)]
    [InlineData(10.5, false)]
    [InlineData(-0.5, false)]
    public void Punten_zijn_hele_of_halve_punten_van_0_tot_10(decimal value, bool valid) => Assert.Equal(valid, FestivalPoints.IsValid(value));

    /// <summary>Nederlands en zonder nul achter de komma, zoals in het rapport en op de website.</summary>
    [Theory]
    [InlineData(8, "8")]
    [InlineData(7.5, "7,5")]
    [InlineData(30.5, "30,5")]
    [InlineData(0, "0")]
    public void Punten_schrijven(decimal value, string shown) => Assert.Equal(shown, FestivalPoints.Format(value));

    [Theory]
    [InlineData("Minioren|Garde|Group|", 1)]
    [InlineData("Junioren|Show|Duo|Character", 2)]
    [InlineData("Jeugd|Garde|Solo|", 3)]
    [InlineData("Hoofdklasse|Show|Group|Modern", 3)]
    public void Delen_van_de_prijsuitreiking(string key, int part) => Assert.Equal(part, FestivalJudgingService.PartOf(key));
}
