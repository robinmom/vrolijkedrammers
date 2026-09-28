using System.Security.Cryptography;
using System.Text;
using Drammers.Modules.Ticketing.Qr;
using Drammers.Modules.Ticketing.Tickets;

namespace Drammers.UnitTests.Ticketing;

/// <summary>Fase 13 (ADR-005): QR-formaat en de validatiebibliotheek die API en scanner (fase 14/15) delen.</summary>
public sealed class TicketQrValidatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2027, 2, 14, 21, 0, 0, TimeSpan.FromHours(1));
    private readonly ECDsa _device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _server = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _ref = RandomNumberGenerator.GetBytes(16);
    private readonly Guid _deviceId = Guid.NewGuid();

    public void Dispose()
    {
        _device.Dispose();
        _server.Dispose();
    }

    private TicketSnapshot Ticket(TicketStatus status = TicketStatus.Active, bool active = true, int cv = 1, Guid? device = null) => new(
        _ref, cv, status, active, Now.AddDays(-1), Now.AddDays(2), QrPayload.ShortDeviceId(device ?? _deviceId),
        _device.ExportSubjectPublicKeyInfo(), "Piet Lid");

    private string Code(ECDsa key, byte version = QrPayload.DeviceSigned, int cv = 1, DateTimeOffset? issued = null, Guid? device = null)
    {
        var unsigned = QrPayload.Unsigned(version, _ref, cv, QrPayload.ShortDeviceId(device ?? _deviceId), (issued ?? Now).ToUnixTimeSeconds(), QrPayload.DefaultValidFor);
        var signature = key.SignData(unsigned, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Base45.Encode([.. unsigned, .. signature]);
    }

    private QrValidation Check(string code, TicketSnapshot? ticket = null) =>
        TicketQrValidator.Validate(code, Now, r => r.AsSpan().SequenceEqual(_ref) ? ticket ?? Ticket() : null, [_server.ExportSubjectPublicKeyInfo()]);

    [Theory]
    [InlineData("AB", "BB8")]
    [InlineData("Hello!!", "%69 VD92EX0")]
    [InlineData("base-45", "UJCLQE7W581")]
    [InlineData("ietf!", "QED8WEX0")]
    public void Base45_volgens_RFC_9285(string input, string encoded)
    {
        Assert.Equal(encoded, Base45.Encode(Encoding.ASCII.GetBytes(input)));
        Assert.Equal(input, Encoding.ASCII.GetString(Base45.TryDecode(encoded)!));
    }

    [Fact]
    public void Code_bevat_geen_persoonsgegevens_en_past_in_QR_versie_6()
    {
        var code = Code(_device);
        var payload = QrPayload.TryDecode(code)!;

        Assert.Equal(146, code.Length);
        Assert.Equal(QrPayload.Length, Base45.TryDecode(code)!.Length);
        Assert.Equal(_ref, payload.Ref);
        Assert.DoesNotContain("PIET", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Geldige_code_van_het_toestel_en_van_de_server()
    {
        Assert.Equal(QrCheck.Valid, Check(Code(_device)).Result);
        Assert.Equal(QrCheck.Valid, Check(Code(_server, QrPayload.ServerSigned)).Result);
    }

    [Fact]
    public void Screenshot_na_45_seconden_plus_klokmarge_is_verlopen()
    {
        Assert.Equal(QrCheck.Valid, Check(Code(_device, issued: Now.AddSeconds(-120))).Result);
        Assert.Equal(QrCheck.Expired, Check(Code(_device, issued: Now.AddSeconds(-140))).Result);
    }

    [Fact]
    public void Nagemaakt_gewijzigd_ander_toestel_of_oude_versie_is_ongeldig()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(QrCheck.InvalidSignature, Check(Code(other)).Result);
        Assert.Equal(QrCheck.InvalidSignature, Check(Code(other, QrPayload.ServerSigned)).Result);
        Assert.Equal(QrCheck.WrongDevice, Check(Code(_device), Ticket(device: Guid.NewGuid())).Result);
        Assert.Equal(QrCheck.Reissued, Check(Code(_device), Ticket(cv: 2)).Result);
        Assert.Equal(QrCheck.Unreadable, Check("NIET EEN CODE").Result);

        var bytes = Base45.TryDecode(Code(_device))!;
        bytes[20] ^= 0x01;
        Assert.Equal(QrCheck.WrongDevice, Check(Base45.Encode(bytes)).Result);
    }

    [Fact]
    public void Geblokkeerd_inactief_en_buiten_carnaval()
    {
        Assert.Equal(("Ticket geblokkeerd.", QrCheck.Blocked), (Check(Code(_device), Ticket(TicketStatus.Blocked)).Message, QrCheck.Blocked));
        Assert.Equal(QrCheck.MembershipInactive, Check(Code(_device), Ticket(active: false)).Result);
        var later = TicketQrValidator.Validate(Code(_device), Now.AddDays(10), _ => Ticket(), []);
        Assert.Equal(QrCheck.OutsideValidity, later.Result);
        Assert.Equal(QrCheck.UnknownTicket, TicketQrValidator.Validate(Code(_device), Now, _ => null, []).Result);
    }

    [Fact]
    public void Code_met_een_spatie_aan_begin_of_eind_blijft_leesbaar()
    {
        // Eerste bytepaar 0x01 0x05: n = 261 en 261 % 45 = 36, het teken voor de spatie.
        var bytes = new byte[QrPayload.Length];
        bytes[0] = QrPayload.DeviceSigned;
        bytes[1] = 5;
        var text = Base45.Encode(bytes);
        Assert.Equal(' ', text[0]);

        var payload = QrPayload.TryDecode(text + "\n");
        Assert.NotNull(payload);
        Assert.Equal(5, payload!.Ref[0]);
    }
}
