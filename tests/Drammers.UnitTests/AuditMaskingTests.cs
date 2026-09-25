using Drammers.Infrastructure.Auditing;

namespace Drammers.UnitTests;

public class AuditMaskingTests
{
    [Fact]
    public void Gevoelige_velden_worden_gemaskeerd_ook_genest()
    {
        var masked = AuditMasking.Apply("""{"name":"Jan","email":"jan@example.com","contact":{"phoneNumber":"0612345678","city":"Loil"},"items":[{"iban":"NL00BANK0123456789"}]}""");

        Assert.Equal("""{"name":"Jan","email":"***","contact":{"phoneNumber":"***","city":"Loil"},"items":[{"iban":"***"}]}""", masked);
    }

    [Fact]
    public void Ongeldige_JSON_wordt_volledig_gemaskeerd() => Assert.Equal("***", AuditMasking.Apply("geen json met jan@example.com"));

    [Fact]
    public void Leeg_blijft_leeg() => Assert.Null(AuditMasking.Apply(null));
}
