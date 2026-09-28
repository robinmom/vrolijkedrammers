using System.Security.Cryptography;
using Drammers.Modules.Ticketing.Tickets;

namespace Drammers.Modules.Ticketing.Qr;

public enum QrCheck
{
    Valid,
    Unreadable,
    UnknownTicket,
    Blocked,
    MembershipInactive,
    OutsideValidity,
    Reissued,
    WrongDevice,
    InvalidSignature,
    Expired,
}

/// <summary>Wat de validatie over een ticket moet weten (online uit de database, offline uit de cache van de scanner).</summary>
public sealed record TicketSnapshot(
    byte[] PublicRef, int CredentialVersion, TicketStatus Status, bool MembershipActive, DateTimeOffset ValidFrom, DateTimeOffset ValidTo,
    byte[]? BoundDeviceShortId, byte[]? BoundDevicePublicKey, string? HolderName);

public sealed record QrValidation(QrCheck Result, string Message, TicketSnapshot? Ticket = null)
{
    public bool IsValid => Result == QrCheck.Valid;
}

/// <summary>
/// Validatie van een QR-code (ADR-005 §5) zonder databasetoegang: dezelfde regels online (API) en offline (scanner).
/// Volgorde: formaat → ticket → status/lidmaatschap/periode → credential_version → toestel → handtekening → tijd.
/// </summary>
public static class TicketQrValidator
{
    /// <summary>Klokmarge tussen toestel en scanner.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(90);

    public static QrValidation Validate(
        string text, DateTimeOffset now, Func<byte[], TicketSnapshot?> findTicket, IReadOnlyList<byte[]> serverPublicKeys)
    {
        var payload = QrPayload.TryDecode(text);
        if (payload is null)
        {
            return new(QrCheck.Unreadable, "Geen geldige QR-code van De Vrolijke Drammers.");
        }

        var ticket = findTicket(payload.Ref);
        if (ticket is null)
        {
            return new(QrCheck.UnknownTicket, "Onbekend ticket.");
        }

        if (ticket.Status == TicketStatus.Blocked)
        {
            return new(QrCheck.Blocked, "Ticket geblokkeerd.", ticket);
        }

        if (!ticket.MembershipActive)
        {
            return new(QrCheck.MembershipInactive, "Geen actief lidmaatschap.", ticket);
        }

        if (now < ticket.ValidFrom || now > ticket.ValidTo)
        {
            return new(QrCheck.OutsideValidity, "Ticket is nu niet geldig (buiten de carnavalsperiode).", ticket);
        }

        if (payload.CredentialVersion != ticket.CredentialVersion)
        {
            return new(QrCheck.Reissued, "Oude code: het ticket is opnieuw uitgegeven.", ticket);
        }

        if (ticket.BoundDeviceShortId is null || !payload.DeviceId.AsSpan().SequenceEqual(ticket.BoundDeviceShortId))
        {
            return new(QrCheck.WrongDevice, "Code van een ander toestel dan waaraan het ticket gekoppeld is.", ticket);
        }

        var unsigned = payload.UnsignedBytes();
        var signed = payload.Version == QrPayload.DeviceSigned
            ? ticket.BoundDevicePublicKey is { } key && Verify(key, unsigned, payload.Signature)
            : serverPublicKeys.Any(k => Verify(k, unsigned, payload.Signature));
        if (!signed)
        {
            return new(QrCheck.InvalidSignature, "Ongeldige handtekening: de code is nagemaakt of gewijzigd.", ticket);
        }

        var issued = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAt);
        if (now < issued - ClockSkew || now > issued.AddSeconds(payload.ValidFor) + ClockSkew)
        {
            return new(QrCheck.Expired, "Verlopen code (bijvoorbeeld een screenshot). Vraag om de live code.", ticket);
        }

        return new(QrCheck.Valid, "Geldig ticket.", ticket);
    }

    public static bool Verify(byte[] subjectPublicKeyInfo, byte[] data, byte[] signature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
