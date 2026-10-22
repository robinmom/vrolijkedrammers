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
    private const string Link = "#066AA6";

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

    /// <summary>Eén factuur; <paramref name="payLink"/> is de Mollie-betaallink (of <c>null</c> als er niet online betaald wordt).</summary>
    public static byte[] Generate(AdvertiserInvoice invoice, InvoiceIssuer issuer, string? payLink = null) => Generate([invoice], issuer, _ => payLink);

    /// <summary>Meerdere facturen in één PDF, elk op een eigen pagina (om te printen); <paramref name="payLink"/> geeft de betaallink per factuur.</summary>
    public static byte[] Generate(IReadOnlyList<AdvertiserInvoice> invoices, InvoiceIssuer issuer, Func<AdvertiserInvoice, string?>? payLink = null) =>
        Document.Create(document =>
        {
            foreach (var invoice in invoices)
            {
                document.Page(page => Page(page, invoice, issuer, payLink?.Invoke(invoice)));
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
        var due = invoice.DueDate.ToString("d MMMM yyyy", Dutch);
        if (invoice.PaymentMethod == InvoicePaymentMethod.Cash)
        {
            if (invoice.PaidAt is { } paid)
            {
                return $"Contant voldaan op {paid.ToString("d MMMM yyyy", Dutch)}. Hartelijk dank voor uw steun!";
            }

            var transfer = issuer.Iban is null ? "" : $", of overmaken op {FormatIban(issuer.Iban)} t.n.v. {issuer.Name} onder vermelding van {invoice.Number}";
            return $"Graag vóór {due} contant voldoen aan de collectant{transfer}.";
        }

        // Online betalen (iDEAL via Mollie): de betaallink staat in de begeleidende e-mail; overmaken onder vermelding
        // van het factuurnummer kan altijd en is zo op het bankafschrift herleidbaar.
        return issuer.Iban is null
            ? $"Betaal met iDEAL via de betaallink in de e-mail, of maak vóór {due} over onder vermelding van {invoice.Number}."
            : $"Betaal met iDEAL via de betaallink in de e-mail, of maak vóór {due} over op {FormatIban(issuer.Iban)} t.n.v. {issuer.Name} onder vermelding van {invoice.Number}.";
    }

    public static string Money(decimal amount) => amount.ToString("C", Dutch);

    private static void Page(PageDescriptor page, AdvertiserInvoice invoice, InvoiceIssuer issuer, string? payLink)
    {
        page.Size(PageSizes.A4);
        page.Margin(48);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontFamily("Inter").FontSize(10).FontColor(Navy).LineHeight(1.4f));

        page.Content().Column(column =>
        {
            // Kop: links het logo met "Factuur" en de ontvanger, rechts de gegevens van de vereniging.
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Width(84).Image(Logo.Value).FitWidth();
                    left.Item().PaddingTop(18).Text("Factuur").FontFamily("Poppins").Bold().FontSize(30).FontColor(Red);
                    left.Item().PaddingTop(22).Text(invoice.CompanyName).Bold();
                    if (invoice.ContactName is { Length: > 0 } contact)
                    {
                        left.Item().Text($"t.a.v. {contact}");
                    }

                    if (invoice.AddressLine is { Length: > 0 } address)
                    {
                        left.Item().Text(address);
                    }

                    if (invoice.PostalCode is not null || invoice.City is not null)
                    {
                        left.Item().Text(string.Join("  ", new[] { invoice.PostalCode, invoice.City }.OfType<string>()));
                    }
                });
                row.ConstantItem(240).Column(from =>
                {
                    from.Item().Text(issuer.Name).FontFamily("Poppins").SemiBold().FontSize(11);
                    foreach (var line in issuer.AddressLines)
                    {
                        from.Item().Text(line);
                    }

                    if (issuer.Phone is { Length: > 0 } phone)
                    {
                        from.Item().Text(t => { t.Span("t  ").SemiBold(); t.Span(phone); });
                    }

                    from.Item().Text(t => { t.Span("e  ").SemiBold(); t.Span(issuer.Email); });
                    if (issuer.Website is { Length: > 0 } website)
                    {
                        from.Item().Text(t => { t.Span("i  ").SemiBold(); t.Span(website); });
                    }

                    if (issuer.Iban is { Length: > 0 } iban)
                    {
                        from.Item().PaddingTop(14).Text(t => { t.Span("IBAN  ").SemiBold(); t.Span(FormatIban(iban)); });
                    }

                    if (issuer.VatNumber is { Length: > 0 } vat)
                    {
                        from.Item().Text(t => { t.Span("Btw-nr  ").SemiBold(); t.Span(vat); });
                    }

                    if (issuer.Kvk is { Length: > 0 } kvk)
                    {
                        from.Item().Text(t => { t.Span("KvK  ").SemiBold(); t.Span(kvk); });
                    }
                });
            });

            // Betaalgegevens (links, grijs) naast de factuurgegevens (rechts).
            column.Item().PaddingTop(34).Row(row =>
            {
                row.RelativeItem().Element(e => PaymentBox(e, invoice, issuer, payLink));
                row.ConstantItem(240).PaddingLeft(24).AlignMiddle().Column(meta => Meta(meta, invoice));
            });

            // De factuurregel(s); de totalen staan onderaan zodat de pagina gevuld is.
            column.Item().PaddingTop(30).Table(table =>
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
            });

            if (invoice.VatRate == 0)
            {
                column.Item().PaddingTop(8).Text("Giften zijn vrijgesteld van btw (geen prestatie).").FontColor(Muted).FontSize(9);
            }
        });

        // De totalen staan onderaan de pagina (in de voettekst), zodat de hele A4 gevuld is.
        page.Footer().Column(footer =>
        {
            footer.Item().AlignRight().Width(260).Element(e => Totals(e, invoice));
            footer.Item().PaddingTop(16).AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span($"Vragen over deze factuur? {issuer.Email} · www.vrolijkedrammers.nl");
            });
        });
    }

    private static void PaymentBox(IContainer container, AdvertiserInvoice invoice, InvoiceIssuer issuer, string? payLink)
    {
        container.Background(Grey).Padding(16).Column(box =>
        {
            box.Spacing(4);
            box.Item().Text("Betaalgegevens").FontFamily("Poppins").SemiBold();
            box.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(t => { t.Span("Te betalen  ").FontColor(Muted); t.Span(Money(invoice.TotalInclVat)).Bold(); });
                if (payLink is { Length: > 0 } link)
                {
                    row.ConstantItem(120).AlignRight().Hyperlink(link).Text("Direct online betalen").FontColor(Link).Underline();
                }
            });

            if (invoice.PaidAt is { } paid)
            {
                box.Item().Text($"Voldaan op {paid.ToString("d MMMM yyyy", Dutch)}").FontColor(Muted).FontSize(9);
            }
            else
            {
                box.Item().Text($"(voor {invoice.DueDate.ToString("d MMMM yyyy", Dutch)})").FontColor(Muted).FontSize(9);
            }

            if (issuer.Iban is { Length: > 0 } iban)
            {
                box.Item().PaddingTop(6).Text(t => { t.Span("Naar IBAN  ").FontColor(Muted); t.Span(FormatIban(iban)).Bold(); });
                box.Item().Text(t => { t.Span("Op naam van  ").FontColor(Muted); t.Span(issuer.Name).Bold(); });
            }

            box.Item().Text(t => { t.Span("Omschrijving  ").FontColor(Muted); t.Span($"Factuur {invoice.Number}").Bold(); });
        });
    }

    private static void Meta(ColumnDescriptor meta, AdvertiserInvoice invoice)
    {
        void Line(string label, string value)
        {
            meta.Item().Row(row =>
            {
                row.RelativeItem().Text(label).FontColor(Muted);
                row.ConstantItem(110).Text(value).SemiBold();
            });
        }

        Line("Factuurnummer", invoice.Number);
        Line("Factuurdatum", invoice.InvoiceDate.ToString("dd-MM-yyyy", Dutch));
        Line("Betalingstermijn", $"{invoice.DueDate.DayNumber - invoice.InvoiceDate.DayNumber} dagen");
        Line("Vervaldatum", invoice.DueDate.ToString("dd-MM-yyyy", Dutch));
    }

    private static void Totals(IContainer container, AdvertiserInvoice invoice)
    {
        var totalVat = invoice.VatAmount + invoice.ServiceFeeVat;
        var subtotal = invoice.Amount + invoice.ServiceFeeNet;
        container.Column(col =>
        {
            void Line(string label, decimal amount, bool muted)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(label).FontColor(muted ? Muted : Navy);
                    row.ConstantItem(110).AlignRight().Text(Money(amount)).FontColor(muted ? Muted : Navy);
                });
            }

            if (totalVat > 0)
            {
                Line("Subtotaal (excl. btw)", subtotal, true);
                Line("Btw 21%", totalVat, true);
            }

            col.Item().PaddingTop(6).BorderTop(1).BorderColor(Navy).PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(totalVat > 0 ? "Totaal incl. btw" : "Totaal").FontFamily("Poppins").SemiBold();
                row.ConstantItem(110).AlignRight().Text(Money(invoice.TotalInclVat)).FontFamily("Poppins").Bold().FontSize(13);
            });
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
