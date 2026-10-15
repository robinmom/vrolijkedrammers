using System.Globalization;
using Drammers.Infrastructure.Advertisers.Invoices;
using Drammers.Modules.Content.Festival;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Drammers.Infrastructure.Festival;

/// <summary>
/// Afdrukken van het Dansgardefestival in de huisstijl (2026-10-15): een blauwe kop met het logo, titels in Poppins, tekst in
/// Inter (zelfde lettertypen en logo als de facturen).
/// </summary>
public static class FestivalPdf
{
    internal const string Navy = "#123047";
    internal const string Red = "#ED0012";
    internal const string Blue = "#087BC1";
    internal const string Muted = "#4A5B69";
    internal const string Grey = "#F0F1F2";
    internal const string Line = "#E2E6EA";
    internal const string Gold = "#FFF3CC";

    internal static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    // Licentie (Community) en lettertypen zet de factuurcode; vóór het eerste document.
    static FestivalPdf() => InvoicePdf.EnsureFonts();

    /// <summary>Het programma op A4 (staand), per onderdeel de tijden en per dans vereniging, naam of titel.</summary>
    public static byte[] Program(ProgramView program) =>
        Document.Create(document => document.Page(page =>
        {
            Setup(page, "Programma", $"{program.FestivalName} · {program.FestivalDate.ToString("dddd d MMMM yyyy", Dutch)} · {program.Location}");
            page.Content().PaddingHorizontal(36).PaddingTop(16).Column(column =>
            {
                column.Spacing(8);
                foreach (var item in program.Items)
                {
                    if (item.Kind == FestivalProgramItemKind.Block)
                    {
                        column.Item().Background(Grey).PaddingVertical(5).PaddingHorizontal(10).Row(row =>
                        {
                            row.ConstantItem(48).Text(item.Start).FontFamily("Poppins").Bold();
                            row.RelativeItem().Text(item.Title).Bold();
                        });
                        continue;
                    }

                    column.Item().ShowEntire().Column(category =>
                    {
                        category.Item().PaddingBottom(2).Row(row =>
                        {
                            row.ConstantItem(48).Text(item.Start).FontFamily("Poppins").Bold().FontColor(Red);
                            row.RelativeItem().Text(item.Title).FontFamily("Poppins").Bold().FontColor(Red);
                            row.ConstantItem(120).AlignRight().Text($"{item.Dances.Count} {(item.Dances.Count == 1 ? "dans" : "dansen")} · tot {item.End}").FontColor(Muted).FontSize(8);
                        });
                        category.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(48);
                                c.RelativeColumn(3);
                                c.RelativeColumn(4);
                            });
                            foreach (var dance in item.Dances)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).Text(dance.Time).FontColor(Muted);
                                table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).Text(dance.Club);
                                table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).Text(dance.Title is { Length: > 0 } title ? $"{title} ({dance.Names})" : dance.Names);
                            }
                        });
                    });
                }
            });
            Footer(page);
        })).WithMetadata(new DocumentMetadata { Title = $"Programma {program.FestivalName}", Author = "De Vrolijke Drammers" }).GeneratePdf();

    /// <summary>
    /// De uitslag per categorie (A4) om af te drukken: plaats, vereniging, naam, punten per onderdeel en totaal. Per deel van
    /// de prijsuitreiking een nieuwe pagina.
    /// </summary>
    public static byte[] Results(FestivalResults results) =>
        Document.Create(document =>
        {
            foreach (var part in results.Categories.GroupBy(c => c.Part).OrderBy(g => g.Key))
            {
                document.Page(page =>
                {
                    Setup(page, "Uitslag", $"{results.FestivalName} · {FestivalJudgingService.PartName(part.Key)}");
                    page.Content().PaddingHorizontal(36).PaddingTop(16).Column(column =>
                    {
                        column.Spacing(12);
                        foreach (var category in part)
                        {
                            column.Item().ShowEntire().Column(c => ResultTable(c, category, null));
                        }
                    });
                    Footer(page);
                });
            }
        }).WithMetadata(new DocumentMetadata { Title = $"Uitslag {results.FestivalName}", Author = "De Vrolijke Drammers" }).GeneratePdf();

    /// <summary>Tabel van één categorie; <paramref name="highlight"/>: rijen van deze inschrijving gemarkeerd (rapport).</summary>
    internal static void ResultTable(ColumnDescriptor column, ResultCategory category, Guid? highlight)
    {
        column.Item().PaddingBottom(3).Row(row =>
        {
            row.RelativeItem().Text(category.Name).FontFamily("Poppins").Bold().FontColor(Red);
            if (!category.Complete)
            {
                row.ConstantItem(140).AlignRight().Text($"{category.Submitted} van {category.Jurors} juryleden").FontSize(8).FontColor(Muted);
            }
        });
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(28);
                c.RelativeColumn(3);
                c.RelativeColumn(3);
                c.ConstantColumn(40);
                c.ConstantColumn(40);
                c.ConstantColumn(40);
                c.ConstantColumn(40);
                c.ConstantColumn(44);
            });
            table.Header(h =>
            {
                foreach (var label in new[] { "Pl.", "Vereniging", "Naam", category.Solo ? "Ritme" : "Synchr.", "Choreo.", "Uitv.", "Moeil.", "Totaal" })
                {
                    h.Cell().Background(Grey).PaddingVertical(3).PaddingHorizontal(4).Text(label).FontSize(8).Bold();
                }
            });
            foreach (var r in category.Rows)
            {
                var mark = highlight is { } id && r.RegistrationId == id;
                IContainer Cell(IContainer c) => c.Background(mark ? Gold : Colors.White).BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).PaddingHorizontal(4);
                table.Cell().Element(Cell).Text(r.Place.ToString(Dutch)).Bold();
                table.Cell().Element(Cell).Text(r.Club);
                table.Cell().Element(Cell).Text(r.Title is { Length: > 0 } title ? $"{title} ({r.Names})" : r.Names);
                foreach (var v in new[] { r.Synchronicity, r.Choreography, r.Execution, r.Difficulty })
                {
                    table.Cell().Element(Cell).AlignRight().Text(v.ToString(Dutch));
                }

                table.Cell().Element(Cell).AlignRight().Text(r.Total.ToString(Dutch)).Bold();
            }
        });
    }

    internal static void Setup(PageDescriptor page, string title, string subtitle)
    {
        page.Size(PageSizes.A4);
        page.Margin(0);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontFamily("Inter").FontSize(9.5f).FontColor(Navy).LineHeight(1.3f));
        page.Header().Background(Blue).PaddingHorizontal(36).PaddingVertical(18).Row(row =>
        {
            row.ConstantItem(56).Height(56).Background(Colors.White).Image(InvoicePdf.LogoPng).FitArea();
            row.RelativeItem().PaddingLeft(14).AlignMiddle().Column(c =>
            {
                c.Item().Text(title).FontFamily("Poppins").Bold().FontSize(20).FontColor(Colors.White);
                c.Item().Text(subtitle).FontSize(9).FontColor(Colors.White);
            });
        });
    }

    internal static void Footer(PageDescriptor page) =>
        page.Footer().PaddingHorizontal(36).PaddingBottom(16).Row(row =>
        {
            row.RelativeItem().Text("Carnavalsvereniging De Vrolijke Drammers Loil · dansgardefestival@vrolijkedrammers.nl").FontSize(7.5f).FontColor(Muted);
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                t.Span("Pagina ");
                t.CurrentPageNumber();
                t.Span(" van ");
                t.TotalPages();
            });
        });
}
