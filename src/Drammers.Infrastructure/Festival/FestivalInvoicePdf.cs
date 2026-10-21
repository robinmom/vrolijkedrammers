using System.Globalization;
using Drammers.Infrastructure.Advertisers.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Drammers.Infrastructure.Festival;

/// <summary>De factuurgegevens van één betaalverzoek van het dansgardefestival (bedragen in centen, excl. btw als netto).</summary>
public sealed record FestivalInvoice(
    string Number, DateOnly Date, string FestivalName, string ClubName, string? ContactName,
    int NetCents, int VatCents, int ServiceFeeNetCents, int ServiceFeeVatCents, int TotalCents);

/// <summary>
/// De factuur van een festival-betaalverzoek als PDF (fase 2b) in de huisstijl: logo, "Factuur" in Drammers Rood, de
/// gegevens van de vereniging en de deelnemer, de btw-uitsplitsing en het totaal. Hergebruikt de lettertypen en het logo
/// van <see cref="InvoicePdf"/>.
/// </summary>
public static class FestivalInvoicePdf
{
    private const string Navy = "#123047";
    private const string Red = "#ED0012";
    private const string Muted = "#4A5B69";
    private const string Grey = "#F0F1F2";

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    public static byte[] Generate(FestivalInvoice inv, InvoiceIssuer issuer)
    {
        InvoicePdf.EnsureFonts();
        return Document.Create(document => document.Page(page => Page(page, inv, issuer)))
            .WithMetadata(new DocumentMetadata { Title = $"Factuur {inv.Number}", Author = issuer.Name, Creator = issuer.Name })
            .GeneratePdf();
    }

    private static string Money(int cents) => (cents / 100m).ToString("C", Dutch);

    private static void Page(PageDescriptor page, FestivalInvoice inv, InvoiceIssuer issuer)
    {
        page.Size(PageSizes.A4);
        page.Margin(48);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontFamily("Inter").FontSize(10).FontColor(Navy).LineHeight(1.4f));

        page.Header().Row(row =>
        {
            row.ConstantItem(72).Image(InvoicePdf.LogoPng).FitArea();
            row.RelativeItem().PaddingLeft(16).AlignMiddle().Column(column =>
            {
                column.Item().Text(issuer.Name).FontFamily("Poppins").SemiBold().FontSize(12);
                column.Item().Text(inv.FestivalName).FontColor(Muted).FontSize(9);
            });
            row.ConstantItem(180).AlignRight().AlignMiddle().Column(column =>
            {
                column.Item().AlignRight().Text("Factuur").FontFamily("Poppins").Bold().FontSize(26).FontColor(Red);
                column.Item().AlignRight().Text(inv.Number).FontFamily("Poppins").SemiBold();
            });
        });

        page.Content().PaddingTop(28).Column(column =>
        {
            column.Spacing(20);
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(to =>
                {
                    to.Item().Text("Aan").FontColor(Muted).FontSize(9);
                    to.Item().Text(inv.ClubName).Bold();
                    if (inv.ContactName is { Length: > 0 } contact)
                    {
                        to.Item().Text($"t.a.v. {contact}");
                    }
                });
                row.ConstantItem(200).Column(from =>
                {
                    from.Item().Text("Van").FontColor(Muted).FontSize(9);
                    from.Item().Text(issuer.Name).Bold();
                    foreach (var line in issuer.AddressLines)
                    {
                        from.Item().Text(line);
                    }

                    if (issuer.Phone is { Length: > 0 } phone)
                    {
                        from.Item().Text(phone);
                    }

                    from.Item().Text(issuer.Email);
                    if (issuer.Iban is { Length: > 0 } iban)
                    {
                        from.Item().Text($"IBAN {FormatIban(iban)}");
                    }

                    if (issuer.Kvk is { Length: > 0 } kvk)
                    {
                        from.Item().Text($"KvK {kvk}");
                    }

                    if (issuer.VatNumber is { Length: > 0 } vat)
                    {
                        from.Item().Text($"Btw-nr {vat}");
                    }
                });
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Factuurdatum: ").FontColor(Muted);
                    t.Span(inv.Date.ToString("d MMMM yyyy", Dutch));
                });
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Factuurnummer: ").FontColor(Muted);
                    t.Span(inv.Number);
                });
            });

            var totalVat = inv.VatCents + inv.ServiceFeeVatCents;
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.ConstantColumn(120);
                });
                table.Header(h =>
                {
                    h.Cell().Background(Grey).Padding(8).Text("Omschrijving").Bold();
                    h.Cell().Background(Grey).Padding(8).AlignRight().Text("Bedrag").Bold();
                });

                void Line(string label, int cents)
                {
                    table.Cell().BorderBottom(1).BorderColor(Grey).Padding(8).Text(label);
                    table.Cell().BorderBottom(1).BorderColor(Grey).Padding(8).AlignRight().Text(Money(cents));
                }

                Line($"Deelname {inv.FestivalName}", inv.NetCents);
                if (inv.ServiceFeeNetCents > 0)
                {
                    Line("Servicekosten (betaling via iDEAL)", inv.ServiceFeeNetCents);
                }

                if (totalVat > 0)
                {
                    Line("Subtotaal (excl. btw)", inv.NetCents + inv.ServiceFeeNetCents);
                    Line("Btw", totalVat);
                }

                table.Cell().Padding(8).AlignRight().Text(totalVat > 0 ? "Totaal incl. btw" : "Totaal").FontFamily("Poppins").SemiBold();
                table.Cell().Padding(8).AlignRight().Text(Money(inv.TotalCents)).FontFamily("Poppins").Bold().FontSize(12);
            });

            var transfer = issuer.Iban is null
                ? $"onder vermelding van {inv.Number}"
                : $"op {FormatIban(issuer.Iban)} t.n.v. {issuer.Name} onder vermelding van {inv.Number}";
            column.Item().Background(Grey).Padding(14)
                .Text($"Betaal met iDEAL via de betaallink in de e-mail, of maak het bedrag over {transfer}.");
            column.Item().Text("Hartelijk dank voor jullie deelname aan het Dansgardefestival. Alaaf!");
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.Span(issuer.Name).FontColor(Muted).FontSize(8);
            if (issuer.Website is { Length: > 0 } website)
            {
                t.Span($" · {website}").FontColor(Muted).FontSize(8);
            }
        });
    }

    private static string FormatIban(string iban) =>
        string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - (i * 4)))));
}
