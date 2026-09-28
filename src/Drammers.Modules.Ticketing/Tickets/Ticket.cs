namespace Drammers.Modules.Ticketing.Tickets;

public enum TicketStatus
{
    Active,
    Blocked,
}

/// <summary>
/// Ledenticket (fase 13, ADR-005): één per lid per carnavalsjaar, geldig de hele carnavalsperiode (OQ-20). Bevat geen
/// persoonsgegevens in de QR: alleen <see cref="PublicRef"/> (128 bit, CSPRNG) en <see cref="CredentialVersion"/>.
/// Eén actieve binding aan een toestel; opnieuw binden op een ander toestel maakt de oude codes direct ongeldig.
/// </summary>
public sealed class Ticket
{
    /// <summary>Zonder tussenkomst van het bestuur kan een ticket zo vaak per carnavalsjaar naar een ander toestel.</summary>
    public const int MaxRebinds = 3;

    public Guid Id { get; set; }

    public int CarnivalYearId { get; set; }

    public Guid MemberId { get; set; }

    public byte[] PublicRef { get; set; } = [];

    public int CredentialVersion { get; set; } = 1;

    public TicketStatus Status { get; set; }

    public string? BlockedReason { get; set; }

    public Guid? BoundDeviceId { get; set; }

    public DateTime? BoundAt { get; set; }

    /// <summary>Aantal keer overgezet naar een ander toestel (de eerste binding telt niet).</summary>
    public int RebindCount { get; set; }

    /// <summary>SHA-256 (hex) van de laatste challenge voor proof-of-possession; verloopt na 5 minuten.</summary>
    public string? BindChallengeHash { get; set; }

    public DateTime? BindChallengeExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Sleutel waarmee de server QR-codes ondertekent voor toestellen zonder hardwaresleutel (fallback, ADR-005 optie 4).
/// De private sleutel staat alleen versleuteld (Data Protection, Key Vault) in de database.
/// </summary>
public sealed class TicketSigningKey
{
    public int Id { get; set; }

    /// <summary>SubjectPublicKeyInfo (DER).</summary>
    public byte[] PublicKey { get; set; } = [];

    public required string ProtectedPrivateKey { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }
}
