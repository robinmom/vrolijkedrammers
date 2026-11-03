using Drammers.Infrastructure.Festival;
using Drammers.Modules.Content.Festival;

namespace Drammers.UnitTests.Festival;

/// <summary>
/// Uitslag van het Dansgardefestival. Artikel 7 van het reglement: hoogste totaal; bij gelijk totaal achtereenvolgens
/// uitvoering, moeilijkheidsgraad en choreografie; blijft het gelijk, dan beslist de voorzitter van de jury en houden de
/// dansen hier een gedeelde plaats.
/// </summary>
public class FestivalRankingTests
{
    private static ResultRow Row(string names, decimal total, decimal execution) =>
        Row(names, total, execution, 0, 0);

    private static ResultRow Row(string names, decimal total, decimal execution, decimal difficulty, decimal choreography) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 0, "Club", names, null, 0, choreography, execution, difficulty, total, []);

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

    /// <summary>
    /// Artikel 7 van het reglement, de hele keten: met hetzelfde totaal en dezelfde uitvoering beslist de
    /// moeilijkheidsgraad, en daarna de choreografie.
    /// </summary>
    [Fact]
    public void Gelijke_uitvoering_moeilijkheidsgraad_dan_choreografie_beslist()
    {
        var ranked = FestivalJudgingService.Rank(
        [
            Row("A", 60, 15, 14, 16),
            Row("B", 60, 15, 15, 10),
            Row("C", 60, 15, 14, 17),
            Row("D", 60, 16, 10, 10),
        ]);

        Assert.Equal([("D", 1), ("B", 2), ("C", 3), ("A", 4)], ranked.Select(r => (r.Names, r.Place)).ToArray());
    }

    /// <summary>
    /// Synchroniteit staat niet in artikel 7: als totaal, uitvoering, moeilijkheidsgraad en choreografie gelijk zijn, is het
    /// gelijkspel en beslist de voorzitter van de jury. Die keuze is geen rekenwerk, dus beide dansen krijgen dezelfde plaats
    /// en de volgende danst krijgt de plaats daarna.
    /// </summary>
    [Fact]
    public void Blijft_het_gelijk_dan_een_gedeelde_plaats_voor_de_voorzitter()
    {
        var ranked = FestivalJudgingService.Rank(
        [
            Row("A", 60, 15, 14, 16) with { Synchronicity = 15 },
            Row("B", 60, 15, 14, 16) with { Synchronicity = 10 },
            Row("C", 55, 14, 13, 14),
        ]);

        Assert.Equal([("A", 1), ("B", 1), ("C", 3)], ranked.Select(r => (r.Names, r.Place)).ToArray());
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
