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

    /// <summary>De verenigingsnaam op de factuur; de SEPA-incassonaam (<see cref="InvoiceIssuer.Name"/>) is daarvoor niet bedoeld.</summary>
    private const string AssociationName = "Carnavalsvereniging De Vrolijke Drammers";

    /// <summary>Breedte van het rechterblok (verenigingsgegevens en factuurgegevens), zodat ze op dezelfde kolom uitlijnen.</summary>
    private const float RightBlock = 224;

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

            var transfer = issuer.Iban is null ? "" : $", of overmaken op {FormatIban(issuer.Iban)} t.n.v. {AssociationName} onder vermelding van {invoice.Number}";
            return $"Graag vóór {due} contant voldoen aan de collectant{transfer}.";
        }

        // Online betalen (iDEAL via Mollie): de betaallink staat in de begeleidende e-mail; overmaken onder vermelding
        // van het factuurnummer kan altijd en is zo op het bankafschrift herleidbaar.
        return issuer.Iban is null
            ? $"U kunt online met iDEAL betalen via de betaallink in de e-mail of op de factuur, of het bedrag vóór {due} overmaken onder vermelding van {invoice.Number}."
            : $"U kunt online met iDEAL betalen via de betaallink in de e-mail of op de factuur, of het bedrag vóór {due} overmaken op {FormatIban(issuer.Iban)} t.n.v. {AssociationName} onder vermelding van {invoice.Number}.";
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
            column.Spacing(20);

            // Boven: links het logo met de naam ernaast; rechts de gegevens van de vereniging (vaste kolombreedte).
            column.Item().Row(row =>
            {
                row.RelativeItem().AlignTop().Row(lr =>
                {
                    lr.ConstantItem(72).Image(Logo.Value).FitWidth();
                    lr.AutoItem().PaddingLeft(16).AlignMiddle().Text("De Vrolijke Drammers").FontFamily("Poppins").SemiBold().FontSize(12);
                });
                row.ConstantItem(RightBlock).AlignTop().Element(e => IssuerBlock(e, issuer));
            });

            // Links de ontvanger met "Factuur" en het nummer; rechts de factuurgegevens, op dezelfde kolom als KvK en ter hoogte van de bedrijfsnaam.
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(to =>
                {
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

                    to.Item().PaddingTop(20).Text("Factuur").FontFamily("Poppins").Bold().FontSize(26).FontColor(Red);
                    to.Item().Text(invoice.Number).FontFamily("Poppins").SemiBold();
                    if (payLink is { Length: > 0 } link)
                    {
                        to.Item().PaddingTop(8).Hyperlink(link).Text("Betaal direct").FontColor(Link).Underline().FontSize(13).Bold();
                    }
                });
                row.ConstantItem(RightBlock).AlignTop().Element(e => InvoiceMeta(e, invoice));
            });

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
            });

            if (invoice.VatRate == 0)
            {
                column.Item().Text("Giften zijn vrijgesteld van btw (geen prestatie).").FontColor(Muted).FontSize(9);
            }
        });

        // Onderaan (duwt vanzelf naar beneden zodat de A4 gevuld is): de totalen, de betaalgegevens en de voettekst.
        page.Footer().Column(footer =>
        {
            footer.Item().AlignRight().Width(260).Element(e => Totals(e, invoice));

            footer.Item().PaddingTop(16).Background(Grey).Padding(14).Text(PaymentText(invoice, issuer));

            footer.Item().PaddingTop(16).AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span($"Vragen over deze factuur? {issuer.Email} · www.vrolijkedrammers.nl");
            });
        });
    }

    private static void Totals(IContainer container, AdvertiserInvoice invoice)
    {
        var totalVat = invoice.VatAmount + invoice.ServiceFeeVat;
        var subtotal = invoice.Amount + invoice.ServiceFeeNet;
        container.Column(col =>
        {
            // De eurotekens in een eigen kolom (links uitgelijnd), de bedragen rechts ernaast, zodat de € netjes onder elkaar staan.
            void Line(string label, decimal amount)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(label).FontColor(Muted);
                    row.ConstantItem(16).Text("€").FontColor(Muted);
                    row.ConstantItem(72).AlignRight().Text(AmountOnly(amount)).FontColor(Muted);
                });
            }

            if (totalVat > 0)
            {
                Line("Subtotaal (excl. btw)", subtotal);
                Line("Btw 21%", totalVat);
            }

            col.Item().PaddingTop(6).BorderTop(1).BorderColor(Navy).PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(totalVat > 0 ? "Totaal incl. btw" : "Totaal").FontFamily("Poppins").SemiBold();
                row.ConstantItem(16).Text("€").FontFamily("Poppins").Bold().FontSize(13);
                row.ConstantItem(72).AlignRight().Text(AmountOnly(invoice.TotalInclVat)).FontFamily("Poppins").Bold().FontSize(13);
            });
        });
    }

    private static string AmountOnly(decimal value) => value.ToString("#,##0.00", Dutch);

    private static void InvoiceMeta(IContainer container, AdvertiserInvoice invoice)
    {
        container.Column(col =>
        {
            void LabelValue(string label, string value)
            {
                col.Item().Row(r =>
                {
                    r.ConstantItem(100).Text(label).SemiBold();
                    r.AutoItem().Text(value);
                });
            }

            LabelValue("Factuurdatum", invoice.InvoiceDate.ToString("dd-MM-yyyy", Dutch));
            LabelValue("Betalingstermijn", $"{invoice.DueDate.DayNumber - invoice.InvoiceDate.DayNumber} dagen");
        });
    }

    private static void IssuerBlock(IContainer container, InvoiceIssuer issuer)
    {
        container.Column(col =>
        {
            col.Item().Text(AssociationName).SemiBold();
            foreach (var line in issuer.AddressLines)
            {
                col.Item().Text(line);
            }

            // t/e/i in een vaste letterkolom, zodat het telefoonnummer, de e-mail en de site netjes gelijk beginnen.
            void Contact(string letter, string value)
            {
                col.Item().Row(r =>
                {
                    r.ConstantItem(16).Text(letter).SemiBold();
                    r.AutoItem().Text(value);
                });
            }

            if (issuer.Phone is { Length: > 0 } phone)
            {
                Contact("t", phone);
            }

            Contact("e", issuer.Email);
            if (issuer.Website is { Length: > 0 } website)
            {
                Contact("i", website);
            }

            void LabelValue(bool first, string label, string value)
            {
                col.Item().PaddingTop(first ? 14 : 0).Row(r =>
                {
                    r.ConstantItem(62).Text(label).SemiBold();
                    r.AutoItem().Text(value);
                });
            }

            var firstFinancial = true;
            if (issuer.Iban is { Length: > 0 } iban)
            {
                LabelValue(firstFinancial, "IBAN", FormatIban(iban));
                firstFinancial = false;
            }

            if (issuer.VatNumber is { Length: > 0 } vat)
            {
                LabelValue(firstFinancial, "Btw-nr", vat);
                firstFinancial = false;
            }

            if (issuer.Kvk is { Length: > 0 } kvk)
            {
                LabelValue(firstFinancial, "KvK", kvk);
            }
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
