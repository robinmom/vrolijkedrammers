using System.Globalization;
using Drammers.Modules.Membership.Advertisers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Drammers.Infrastructure.Advertisers.Invoices;

/// <summary>De gegevens van de vereniging op de factuur: naam en IBAN (zoals bij de incasso), adres, KvK- en btw-nummer, telefoon en website.</summary>
public sealed record InvoiceIssuer(
    string Name, string? Iban, IReadOnlyList<string> AddressLines, string? Kvk, string Email, string? VatNumber = null, string? Phone = null, string? Website = null);

/// <summary>
/// De factuur als PDF (fase 27e) in de huisstijl: logo, "Factuur" in Drammers Rood (Poppins), tekst in Inter, de
/// gegevens van de vereniging en de adverteerder, het bedrag en hoe er betaald wordt (incasso of contant).
/// QuestPDF is gratis voor verenigingen (Community-licentie).
/// </summary>
public static class InvoicePdf
{
    private const string Navy = "#123047";
    private const string Red = "#ED0012";
    private const string Muted = "#4A5B69";
    private const string Grey = "#F0F1F2";

    private static readonly Lazy<byte[]> Logo = new(() => Read("logo.png"));

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    static InvoicePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Alleen de meegeleverde lettertypen; geen zoektocht naar systeemlettertypen (die er op Linux niet zijn).
        QuestPDF.Settings.UseSystemFonts = false;
        foreach (var font in new[] { "Inter-400.ttf", "Inter-700.ttf", "Poppins-SemiBold.ttf", "Poppins-Bold.ttf" })
        {
            using var stream = new MemoryStream(Read(font));
            QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
        }
    }

    /// <summary>Het logo (PNG) voor andere documenten in de huisstijl; registreert ook de lettertypen.</summary>
    public static byte[] LogoPng => Logo.Value;

    /// <summary>Andere documenten in de huisstijl (festival): zorgt dat Inter en Poppins zijn geregistreerd.</summary>
    public static void EnsureFonts() => _ = Logo;

    /// <summary>Eén factuur.</summary>
    public static byte[] Generate(AdvertiserInvoice invoice, InvoiceIssuer issuer) => Generate([invoice], issuer);

    /// <summary>Meerdere facturen in één PDF, elk op een eigen pagina (om te printen).</summary>
    public static byte[] Generate(IReadOnlyList<AdvertiserInvoice> invoices, InvoiceIssuer issuer) =>
        Document.Create(document =>
        {
            foreach (var invoice in invoices)
            {
                document.Page(page => Page(page, invoice, issuer));
            }
        }).WithMetadata(new DocumentMetadata { Title = invoices.Count == 1 ? $"Factuur {invoices[0].Number}" : "Facturen", Author = issuer.Name, Creator = issuer.Name })
        .GeneratePdf();

    /// <summary>De omschrijving op de factuur, bijvoorbeeld "Advertentie Drammerskrant 2027".</summary>
    public static string Description(AdvertiserKind kind, int year) => kind switch
    {
        AdvertiserKind.Advertisement => $"Advertentie Drammerskrant {year}",
        AdvertiserKind.FreeGift => $"Vrije gift {year}",
        _ => $"Gift {year}",
    };

    /// <summary>Hoe er betaald wordt; ook gebruikt in de e-mail bij de factuur.</summary>
    public static string PaymentText(AdvertiserInvoice invoice, InvoiceIssuer issuer)
    {
        if (invoice.PaymentMethod == InvoicePaymentMethod.Cash)
        {
            if (invoice.PaidAt is { } paid)
            {
                return $"Contant voldaan op {paid.ToString("d MMMM yyyy", Dutch)}. Hartelijk dank voor uw steun!";
            }

            var transfer = issuer.Iban is null ? "" : $", of overmaken op {FormatIban(issuer.Iban)} t.n.v. {issuer.Name} onder vermelding van {invoice.Number}";
            return $"Graag binnen 14 dagen contant voldoen aan de collectant{transfer}.";
        }

        // Online betalen (iDEAL via Mollie): de betaallink komt in een latere fase in de begeleidende e-mail; overmaken
        // onder vermelding van het factuurnummer kan altijd en is zo op het bankafschrift herleidbaar.
        return issuer.Iban is null
            ? $"Graag binnen 14 dagen voldoen onder vermelding van {invoice.Number}."
            : $"Graag binnen 14 dagen overmaken op {FormatIban(issuer.Iban)} t.n.v. {issuer.Name} onder vermelding van {invoice.Number}.";
    }

    public static string Money(decimal amount) => amount.ToString("C", Dutch);

    private static void Page(PageDescriptor page, AdvertiserInvoice invoice, InvoiceIssuer issuer)
    {
        page.Size(PageSizes.A4);
        page.Margin(48);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontFamily("Inter").FontSize(10).FontColor(Navy).LineHeight(1.4f));

        page.Header().Row(row =>
        {
            row.ConstantItem(72).Image(Logo.Value).FitArea();
            row.RelativeItem().PaddingLeft(16).AlignMiddle().Column(column =>
            {
                column.Item().Text(issuer.Name).FontFamily("Poppins").SemiBold().FontSize(12);
                column.Item().Text("Carnavalsvereniging Loil · sinds 1958").FontColor(Muted).FontSize(9);
            });
            row.ConstantItem(180).AlignRight().AlignMiddle().Column(column =>
            {
                column.Item().AlignRight().Text("Factuur").FontFamily("Poppins").Bold().FontSize(26).FontColor(Red);
                column.Item().AlignRight().Text(invoice.Number).FontFamily("Poppins").SemiBold();
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
                    to.Item().Text(invoice.CompanyName).Bold();
                    if (invoice.ContactName is { Length: > 0 } contact)
                    {
                        to.Item().Text($"t.a.v. {contact}");
                    }

                    if (invoice.AddressLine is { Length: > 0 } address)
                    {
                        to.Item().Text(address);
                    }

                    if (invoice.PostalCode is not null || invoice.City is not null)
                    {
                        to.Item().Text(string.Join("  ", new[] { invoice.PostalCode, invoice.City }.OfType<string>()));
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
                    if (issuer.Website is { Length: > 0 } website)
                    {
                        from.Item().Text(website);
                    }

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
                    t.Span(invoice.InvoiceDate.ToString("d MMMM yyyy", Dutch));
                });
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Factuurnummer: ").FontColor(Muted);
                    t.Span(invoice.Number);
                });
            });

            var totalVat = invoice.VatAmount + invoice.ServiceFeeVat;
            var subtotal = invoice.Amount + invoice.ServiceFeeNet;
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

                void Line(string label, decimal amount)
                {
                    table.Cell().BorderBottom(1).BorderColor(Grey).Padding(8).Text(label);
                    table.Cell().BorderBottom(1).BorderColor(Grey).Padding(8).AlignRight().Text(Money(amount));
                }

                Line(invoice.Description, invoice.Amount);
                if (invoice.ServiceFeeNet > 0)
                {
                    Line("Servicekosten (betaling via iDEAL)", invoice.ServiceFeeNet);
                }

                if (totalVat > 0)
                {
                    Line("Subtotaal (excl. btw)", subtotal);
                    Line("Btw 21%", totalVat);
                }

                table.Cell().Padding(8).AlignRight().Text(totalVat > 0 ? "Totaal incl. btw" : "Totaal").FontFamily("Poppins").SemiBold();
                table.Cell().Padding(8).AlignRight().Text(Money(invoice.TotalInclVat)).FontFamily("Poppins").Bold().FontSize(12);
            });

            if (invoice.VatRate == 0)
            {
                column.Item().Text("Giften zijn vrijgesteld van btw (geen prestatie).").FontColor(Muted).FontSize(9);
            }

            column.Item().Background(Grey).Padding(14).Text(PaymentText(invoice, issuer));
            column.Item().Text("Hartelijk dank voor uw bijdrage aan het carnaval in Loil. Alaaf!");
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
            t.Span($"Vragen over deze factuur? {issuer.Email} · www.vrolijkedrammers.nl");
        });
    }

    private static string FormatIban(string iban) => string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - (i * 4)))));

    private static byte[] Read(string name)
    {
        var assembly = typeof(InvoicePdf).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith($".Invoices.Assets.{name}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
