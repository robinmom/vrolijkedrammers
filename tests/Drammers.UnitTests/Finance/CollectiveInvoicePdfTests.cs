using System.Text;
using Drammers.Infrastructure.Finance;

namespace Drammers.UnitTests.Finance;

public class CollectiveInvoicePdfTests
{
    [Fact]
    public void Genereert_een_pdf_met_de_regels_en_een_totaal()
    {
        var lines = new List<CollectiveInvoiceLineView>
        {
            new("Movement-IT B.V.", "ADV-2027-0001", new DateOnly(2027, 1, 20), 217.80m),
            new("Smashstudios", "ADV-2027-0002", new DateOnly(2027, 1, 22), 70m),
        };

        var pdf = CollectiveInvoicePdf.Generate("Adverteerders", "2026/2027", "VF-2027-ADV-01", new DateOnly(2027, 2, 1), lines, 287.80m);

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4), StringComparison.Ordinal);
        Assert.True(pdf.Length > 5000);
    }
}
