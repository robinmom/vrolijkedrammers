using System.Globalization;
using Drammers.Infrastructure.Advertisers.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Drammers.Infrastructure.Finance;

/// <summary>
/// De verzamelfactuur als PDF (fase 4d) in de huisstijl: per onderdeel en carnavalsjaar de betaalde transacties regel
/// voor regel (partij, kenmerk, betaaldatum en bedrag) met een totaal onderaan. Hergebruikt het logo en de lettertypen
/// van de gewone factuur.
/// </summary>
public static class CollectiveInvoicePdf
{
    private const string Navy = "#123047";
    private const string Red = "#ED0012";
    private const string Muted = "#4A5B69";
    private const string Grey = "#F0F1F2";
    private const string AssociationName = "Carnavalsvereniging De Vrolijke Drammers";

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    public static byte[] Generate(
        string sourceLabel, string year, string number, DateOnly invoiceDate, IReadOnlyList<CollectiveInvoiceLineView> lines, decimal total)
    {
        InvoicePdf.EnsureFonts();
        return Document.Create(document =>
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontFamily("Inter").FontSize(10).FontColor(Navy).LineHeight(1.4f));

                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        row.ConstantItem(42).Height(42).Image(InvoicePdf.LogoPng);
                        row.AutoItem().PaddingLeft(16).AlignMiddle().Text(AssociationName).FontFamily("Poppins").SemiBold().FontSize(12);
                    });
                    header.Item().PaddingTop(18).Text("Verzamelfactuur").FontFamily("Poppins").Bold().FontSize(26).FontColor(Red);
                    header.Item().Text($"{sourceLabel} · carnavalsjaar {year}").FontColor(Muted);
                    header.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Text(t =>
                        {
                            t.Line("Factuurnummer").FontColor(Muted).FontSize(9);
                            t.Line(number).FontFamily("Poppins").SemiBold();
                        });
                        row.RelativeItem().AlignRight().Text(t =>
                        {
                            t.Line("Factuurdatum").FontColor(Muted).FontSize(9);
                            t.Line(invoiceDate.ToString("d MMMM yyyy", Dutch));
                        });
                    });
                });

                page.Content().PaddingTop(16).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2);
                        columns.ConstantColumn(90);
                        columns.ConstantColumn(80);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Background(Grey).Padding(8).Text("Partij").Bold();
                        h.Cell().Background(Grey).Padding(8).Text("Kenmerk").Bold();
                        h.Cell().Background(Grey).Padding(8).Text("Betaald op").Bold();
                        h.Cell().Background(Grey).Padding(8).AlignRight().Text("Bedrag").Bold();
                    });
                    foreach (var line in lines)
                    {
                        table.Cell().BorderBottom(1).BorderColor(Grey).Padding(8).Text(line.Party);
                        table.Cell().BorderBottom(1).BorderColor(Grey).Padding(8).Text(line.Reference).FontColor(Muted);
                        table.Cell().BorderBottom(1).BorderColor(Grey).Padding(8).Text(line.PaidOn.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)).FontColor(Muted);
                        table.Cell().BorderBottom(1).BorderColor(Grey).Padding(8).AlignRight().Text(Money(line.Amount));
                    }

                    table.Cell().ColumnSpan(3).PaddingTop(4).Padding(8).Text("Totaal").FontFamily("Poppins").SemiBold();
                    table.Cell().PaddingTop(4).Padding(8).AlignRight().Text(Money(total)).FontFamily("Poppins").SemiBold();
                });

                page.Footer().PaddingTop(16).Text($"{lines.Count} {(lines.Count == 1 ? "transactie" : "transacties")} · {AssociationName}")
                    .FontColor(Muted).FontSize(8);
            })).GeneratePdf();
    }

    private static string Money(decimal value) => "€ " + value.ToString("N2", Dutch);
}
