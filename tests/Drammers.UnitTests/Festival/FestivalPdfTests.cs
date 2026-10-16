using Drammers.Infrastructure.Festival;
using Drammers.Modules.Content.Festival;

namespace Drammers.UnitTests.Festival;

/// <summary>Afdrukken van het Dansgardefestival in de huisstijl (2026-10-15).</summary>
public class FestivalPdfTests
{
    [Fact]
    public void Programma_als_A4_pdf()
    {
        var dances = new[] { new ProgramDance(Guid.NewGuid(), Guid.NewGuid(), 1, "10:35", "CV De Deurdouwers", "ShowSparks", null, 8, 7.5m) };
        var program = new ProgramView(Guid.NewGuid(), "Dansgardefestival 2027", new DateOnly(2027, 2, 21), "Hart van Loil", "10:30", "11:00", null, 1, 1,
        [
            new ProgramItemView(Guid.NewGuid(), FestivalProgramItemKind.Block, "Opening", null, "10:30", "10:35", false, 5, false, []),
            new ProgramItemView(Guid.NewGuid(), FestivalProgramItemKind.Category, "Minioren Garde", "Minioren|Garde|Group|", "10:35", "10:39", false, 4, false, dances),
        ]);

        var pdf = FestivalPdf.Program(program);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Juryrapport_als_pdf_met_uitslag()
    {
        var registration = Guid.NewGuid();
        var rows = new[] { new ResultRow(Guid.NewGuid(), registration, 1, "CV De Deurdouwers", "Lynn Knuman", null, 18, 18, 18, 18, 72, [36, 36]) };
        var results = new FestivalResults(Guid.NewGuid(), "Dansgardefestival 2027", new DateOnly(2027, 2, 21), "Hart van Loil", [],
            [new ResultCategory("Senioren|Garde|Solo|", "Senioren Solo Garde", 3, true, 2, 2, true, rows)], true, null, null, null);
        var dance = new ReportDance("Gardedans · Solo · Senioren", "Gardedans", "Lynn Knuman", "Lynn Knuman · 1 danser · 16 jr", true, 1, 1, 72,
            [new ReportJuror(1, [9, 9, 9, 9]), new ReportJuror(2, [9, 9, 9, 9])], [new ReportComment(2, "Sterke uitstraling")]);

        var pdf = FestivalPdf.Report(new FestivalReport(registration, "CV De Deurdouwers", results, [dance]));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(FestivalPdf.Results(results), 0, 4));
    }
}
