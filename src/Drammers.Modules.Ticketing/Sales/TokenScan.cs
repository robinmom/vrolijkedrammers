namespace Drammers.Modules.Ticketing.Sales;

public enum TokenScanOutcome
{
    /// <summary>Geldige munten-QR: de kassa ziet de bestelling en kan uitgeven.</summary>
    Ready,

    /// <summary>"Bestelling uitgegeven" is ingedrukt.</summary>
    Issued,

    /// <summary>Geweigerd, met de reden (bijv. al uitgegeven, ander toestel, verlopen code).</summary>
    Refused,
}

/// <summary>
/// Kassalog (fase 19c): elke scan van een munten-QR bij de kassa en elke uitgifte. Records worden niet verwijderd; bij
/// uitgeven wordt dezelfde regel bijgewerkt (<see cref="Outcome"/> Issued, <see cref="IssuedAt"/>).
/// </summary>
public sealed class TokenScan
{
    public Guid Id { get; set; }

    public Guid? OrderTicketId { get; set; }

    public Guid? OrderId { get; set; }

    /// <summary>Het lid van de munten; bij het verwijderen van het lid leeggemaakt (AVG).</summary>
    public Guid? MemberId { get; set; }

    public int? Quantity { get; set; }

    public TokenScanOutcome Outcome { get; set; }

    /// <summary>Reden bij weigeren, bijv. <c>AlreadyIssued</c> of <c>WrongDevice</c>.</summary>
    public string? Reason { get; set; }

    public Guid OperatorUserId { get; set; }

    public Guid? OperatorDeviceId { get; set; }

    public DateTime ScannedAt { get; set; }

    public DateTime? IssuedAt { get; set; }
}
