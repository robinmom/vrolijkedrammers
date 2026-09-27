using System.Security.Cryptography;
using System.Text.Json;

namespace Drammers.UnitTests.DeviceKey;

/// <summary>
/// Spike OQ-68 (ADR-005): handtekeningen van de hardwaresleutel moeten in .NET (API en scanner) te controleren zijn.
/// De vectoren komen uit <c>DeviceKeyStore.swift</c> (Security-framework, dezelfde conversies als op iOS): publieke
/// sleutel als SubjectPublicKeyInfo, handtekening als r‖s (IEEE P1363, 64 bytes), ook bij DER-lengtes 70/71/72.
/// </summary>
public class DeviceKeySignatureTests
{
    private sealed record Vector(string PublicKey, string Data, string Signature, string DerLength);

    public static TheoryData<string> Files => new() { "ios-security-framework.json" };

    private static List<Vector> Load(string file) =>
        JsonSerializer.Deserialize<List<Vector>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DeviceKey", file)), JsonSerializerOptions.Web)!;

    private static bool Verify(Vector v, byte[]? data = null)
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(v.PublicKey), out _);
        return key.VerifyData(
            data ?? Convert.FromBase64String(v.Data), Convert.FromBase64String(v.Signature), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Handtekeningen_van_het_toestel_zijn_geldig_in_dotnet(string file)
    {
        var vectors = Load(file);

        Assert.Equal(["70", "71", "72"], vectors.Select(v => v.DerLength).Distinct().Order());
        Assert.All(vectors, v =>
        {
            Assert.Equal(64, Convert.FromBase64String(v.Signature).Length);
            Assert.True(Verify(v));
        });
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Gewijzigde_payload_is_ongeldig(string file)
    {
        var vector = Load(file)[0];
        var data = Convert.FromBase64String(vector.Data);
        data[^1] ^= 0x01;

        Assert.False(Verify(vector, data));
    }
}
