using System.Buffers.Binary;

namespace Drammers.Modules.Ticketing.Qr;

/// <summary>
/// QR-payload (ADR-005): vaste binaire layout van 97 bytes, base45-gecodeerd (RFC 9285).
/// <c>| v 1 | ref 16 | cv 2 | did 8 | iat 4 | exp 2 | sig 64 |</c>. Versie 1 is ondertekend met de hardwaresleutel van
/// het toestel, versie 2 door de server (fallback). De handtekening is ECDSA P-256/SHA-256 als r‖s over de eerste 33 bytes.
/// </summary>
public sealed record QrPayload(byte Version, byte[] Ref, int CredentialVersion, byte[] DeviceId, long IssuedAt, int ValidFor, byte[] Signature)
{
    public const byte DeviceSigned = 1;
    public const byte ServerSigned = 2;
    public const int UnsignedLength = 33;
    public const int SignatureLength = 64;
    public const int Length = UnsignedLength + SignatureLength;

    /// <summary>Standaardgeldigheid van één code; de app ververst elke 30 seconden.</summary>
    public const int DefaultValidFor = 45;

    public static byte[] Unsigned(byte version, byte[] reference, int credentialVersion, byte[] deviceId, long issuedAt, int validFor)
    {
        if (reference.Length != 16 || deviceId.Length != 8)
        {
            throw new ArgumentException("ref moet 16 bytes zijn en did 8 bytes.");
        }

        var bytes = new byte[UnsignedLength];
        bytes[0] = version;
        reference.CopyTo(bytes, 1);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(17), checked((ushort)credentialVersion));
        deviceId.CopyTo(bytes, 19);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(27), checked((uint)issuedAt));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(31), checked((ushort)validFor));
        return bytes;
    }

    public byte[] UnsignedBytes() => Unsigned(Version, Ref, CredentialVersion, DeviceId, IssuedAt, ValidFor);

    public string Encode() => Base45.Encode([.. UnsignedBytes(), .. Signature]);

    /// <returns>De payload, of <c>null</c> als het geen geldige QR van De Vrolijke Drammers is.</returns>
    public static QrPayload? TryDecode(string text)
    {
        // Geen Trim(): de spatie is een geldig base45-teken en kan aan het begin of eind van een code staan.
        var bytes = Base45.TryDecode(text.Trim('\r', '\n', '\t'));
        if (bytes is not { Length: Length } || bytes[0] is not (DeviceSigned or ServerSigned))
        {
            return null;
        }

        return new QrPayload(
            bytes[0], bytes[1..17], BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(17)), bytes[19..27],
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(27)), BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(31)), bytes[33..]);
    }

    /// <summary>Verkorte device-id in de QR: de eerste 8 bytes van de device-GUID.</summary>
    public static byte[] ShortDeviceId(Guid deviceId) => deviceId.ToByteArray()[..8];
}

/// <summary>Base45 (RFC 9285): per 2 bytes 3 tekens, een laatste losse byte 2 tekens; past in de alfanumerieke QR-modus.</summary>
public static class Base45
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[(bytes.Length / 2 * 3) + (bytes.Length % 2 * 2)];
        var o = 0;
        for (var i = 0; i < bytes.Length; i += 2)
        {
            if (i + 1 < bytes.Length)
            {
                var n = (bytes[i] * 256) + bytes[i + 1];
                chars[o++] = Alphabet[n % 45];
                chars[o++] = Alphabet[n / 45 % 45];
                chars[o++] = Alphabet[n / 2025];
            }
            else
            {
                chars[o++] = Alphabet[bytes[i] % 45];
                chars[o++] = Alphabet[bytes[i] / 45];
            }
        }

        return new string(chars);
    }

    public static byte[]? TryDecode(string text)
    {
        if (text.Length % 3 == 1)
        {
            return null;
        }

        var bytes = new List<byte>(text.Length / 3 * 2 + 1);
        for (var i = 0; i < text.Length; i += 3)
        {
            var c = new int[Math.Min(3, text.Length - i)];
            for (var j = 0; j < c.Length; j++)
            {
                c[j] = Alphabet.IndexOf(text[i + j], StringComparison.Ordinal);
                if (c[j] < 0)
                {
                    return null;
                }
            }

            if (c.Length == 3)
            {
                var n = c[0] + (c[1] * 45) + (c[2] * 2025);
                if (n > 0xFFFF)
                {
                    return null;
                }

                bytes.Add((byte)(n >> 8));
                bytes.Add((byte)(n & 0xFF));
            }
            else
            {
                var n = c[0] + (c[1] * 45);
                if (n > 0xFF)
                {
                    return null;
                }

                bytes.Add((byte)n);
            }
        }

        return [.. bytes];
    }
}
