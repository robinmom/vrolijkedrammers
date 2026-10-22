using System.Text;
using Drammers.Infrastructure.Advertisers.Invoices;
using Drammers.Modules.Membership.Advertisers;

namespace Drammers.UnitTests.Advertisers;

public class InvoicePdfTests
{
    private static readonly InvoiceIssuer Issuer = new(
        "Carnavalsvereniging De Vrolijke Drammers", "NL54RABO0314831886", ["Schuttersstraat 24", "6941EA Didam"],
        "40122564", "penningmeester@vrolijkedrammers.nl", "NL005844824B01", "+31620534320", "www.vrolijkedrammers.nl");

    private static AdvertiserInvoice Invoice() => new()
    {
        Id = Guid.NewGuid(),
        Number = "ADV-2027-0001",
        Year = 2027,
        Sequence = 1,
        InvoiceDate = new DateOnly(2026, 2, 2),
        DueDate = new DateOnly(2026, 2, 16),
        Amount = 35m,
        VatRate = 0.21m,
        VatAmount = 7.35m,
        TotalInclVat = 42.35m,
        Description = "Advertentie Drammerskrant 2027",
        PaymentMethod = InvoicePaymentMethod.Mollie,
        Status = InvoiceStatus.Sent,
        CompanyName = "Movement-IT B.V.",
        AddressLine = "Korenbloemhof",
        PostalCode = "6942TB",
        City = "Didam",
    };

    [Fact]
    public void Genereert_een_pdf_met_betaallink()
    {
        var pdf = InvoicePdf.Generate(Invoice(), Issuer, "https://www.vrolijkedrammers.nl/api/v1/advertisers/invoices/x/pay?t=abc");

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4), StringComparison.Ordinal);
        Assert.True(pdf.Length > 5000);
    }

    [Fact]
    public void Werkt_zonder_betaallink()
    {
        var pdf = InvoicePdf.Generate(Invoice(), Issuer);

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4), StringComparison.Ordinal);
    }
}
