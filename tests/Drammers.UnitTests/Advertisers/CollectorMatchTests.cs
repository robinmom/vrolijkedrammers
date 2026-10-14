using Drammers.Infrastructure.Advertisers;

namespace Drammers.UnitTests.Advertisers;

public class CollectorMatchTests
{
    private static readonly Guid Femke = Guid.NewGuid();
    private static readonly Guid Alfred = Guid.NewGuid();
    private static readonly AdvertiserCollector[] Collectors = [new(Femke, "Femke Franken-Peters"), new(Alfred, "Alfred Rasing")];

    [Theory]
    [InlineData("FEMKE PETERS")]
    [InlineData("Femke Franken")]
    [InlineData("femke franken-peters")]
    public void Dubbele_achternaam_wordt_herkend(string name) => Assert.Equal(Femke, AdvertiserAdministration.MatchCollector(Collectors, name));

    [Theory]
    [InlineData("Femke Jansen")]
    [InlineData("Peters")]
    [InlineData("Anouk Berendsen")]
    public void Andere_namen_niet(string name) => Assert.Null(AdvertiserAdministration.MatchCollector(Collectors, name));

    [Fact]
    public void Twee_kandidaten_is_geen_match() =>
        Assert.Null(AdvertiserAdministration.MatchCollector([.. Collectors, new(Guid.NewGuid(), "Femke de Peters")], "Femke Peters"));
}
