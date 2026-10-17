using Drammers.Infrastructure.Exports;

namespace Drammers.UnitTests.Exports;

/// <summary>M8: geen formule-injectie in CSV-exports (Excel voert cellen die met = + - @ beginnen uit).</summary>
public class CsvTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"https://evil\";\"klik\")", "\"'=HYPERLINK(\"\"https://evil\"\";\"\"klik\"\")\"")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+31612345678", "'+31612345678")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("\r=1", "\"'\r=1\"")]
    public void Formules_krijgen_een_apostrof(string value, string expected) => Assert.Equal(expected, Csv.Cell(value));

    [Theory]
    [InlineData("Garde Eerste", "Garde Eerste")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("zeg \"hoi\"", "\"zeg \"\"hoi\"\"\"")]
    [InlineData("regel\nregel", "\"regel\nregel\"")]
    [InlineData("12,5", "12,5")]
    [InlineData("a=b", "a=b")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Gewone_waarden_blijven_gelijk_en_worden_gequote_waar_nodig(string? value, string expected) =>
        Assert.Equal(expected, Csv.Cell(value));

    [Fact]
    public void Rij_met_puntkomma_als_scheidingsteken() =>
        Assert.Equal("1;'=evil;\"x;y\"", Csv.Row(["1", "=evil", "x;y"]));
}
