namespace Drammers.Modules.Ticketing.Tickets;

public enum AccessMethod
{
    Qr,
    Manual,
}

/// <summary>Uitkomst voor het deurpersoneel: groen, groen-herhaald, oranje (beslissen) of rood.</summary>
public enum AccessOutcome
{
    Admitted,
    AdmittedAgain,
    Warning,
    Refused,
}

public enum AccessDecision
{
    Admitted,
    Refused,
}

/// <summary>
/// Eén toegangspoging bij een activiteit met toegangscontrole (fase 14, ADR-006): een QR-scan in de app of handmatig
/// inchecken in het portal. Records worden nooit overschreven; alleen de beslissing bij oranje wordt aangevuld.
/// Ook ongeldige scans worden vastgelegd (zonder de code zelf).
/// </summary>
public sealed class AccessScan
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public Guid? TicketId { get; set; }

    public Guid? MemberId { get; set; }

    public AccessMethod Method { get; set; }

    public AccessOutcome Outcome { get; set; }

    /// <summary>Reden bij rood of oranje, bijv. <c>Blocked</c> of <c>OtherDevice</c>.</summary>
    public string? Reason { get; set; }

    public AccessDecision? Decision { get; set; }

    public Guid OperatorUserId { get; set; }

    public Guid? OperatorDeviceId { get; set; }

    public DateTime ScannedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>Telt als "binnen": groen, of oranje met "Toch toelaten".</summary>
    public bool Admits => Outcome is AccessOutcome.Admitted or AccessOutcome.AdmittedAgain
        || (Outcome == AccessOutcome.Warning && Decision == AccessDecision.Admitted);
}
