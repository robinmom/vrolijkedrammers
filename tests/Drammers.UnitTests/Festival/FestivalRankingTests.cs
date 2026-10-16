using Drammers.Infrastructure.Festival;

namespace Drammers.UnitTests.Festival;

/// <summary>Uitslag van het Dansgardefestival: hoogste totaal; gelijk → hoogste uitvoering; nog gelijk → gedeelde plaats.</summary>
public class FestivalRankingTests
{
    private static ResultRow Row(string names, int total, int execution) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 0, "Club", names, null, 0, 0, execution, 0, total, []);

    [Fact]
    public void Gelijk_totaal_hoogste_uitvoering_wint_anders_gedeelde_plaats()
    {
        var ranked = FestivalJudgingService.Rank([Row("A", 60, 15), Row("B", 62, 16), Row("C", 62, 17), Row("D", 60, 15), Row("E", 50, 20)]);
        Assert.Equal([("C", 1), ("B", 2), ("A", 3), ("D", 3), ("E", 5)], ranked.Select(r => (r.Names, r.Place)).ToArray());
    }

    [Theory]
    [InlineData("Minioren|Garde|Group|", 1)]
    [InlineData("Junioren|Show|Duo|Character", 2)]
    [InlineData("Jeugd|Garde|Solo|", 3)]
    [InlineData("Senioren|Show|Group|Modern", 3)]
    public void Delen_van_de_prijsuitreiking(string key, int part) => Assert.Equal(part, FestivalJudgingService.PartOf(key));
}
