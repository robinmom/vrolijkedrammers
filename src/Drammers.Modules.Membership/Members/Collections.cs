namespace Drammers.Modules.Membership.Members;

/// <summary>
/// Een incassorun (fase 23c): de contributie van de betalende leden op één incassodatum, als pain.008-bestand voor de
/// bank. Bedragen, kenmerken en de laatste 4 tekens van de IBAN worden vastgelegd; de volledige IBAN alleen bij het
/// maken van het bestand (ontsleuteld in het geheugen).
/// </summary>
public sealed class CollectionRun
{
    public Guid Id { get; set; }

    public DateOnly CollectionDate { get; set; }

    public required string Description { get; set; }

    /// <summary>Uniek berichtkenmerk in het bestand (max. 35 tekens).</summary>
    public required string MessageId { get; set; }

    public int LineCount { get; set; }

    public decimal Total { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    /// <summary>Wanneer het bestand voor het laatst is gedownload.</summary>
    public DateTime? ExportedAt { get; set; }
}

public enum SequenceType
{
    /// <summary>Eerste incasso onder een nieuwe machtiging.</summary>
    Frst,

    /// <summary>Volgende incasso onder een bestaande machtiging.</summary>
    Rcur,
}

public sealed class CollectionRunLine
{
    public long Id { get; set; }

    public Guid RunId { get; set; }

    public Guid MemberId { get; set; }

    public required string MemberNumber { get; set; }

    public required string DebtorName { get; set; }

    public decimal Amount { get; set; }

    public required string MandateReference { get; set; }

    public DateOnly? MandateSignedOn { get; set; }

    public SequenceType SequenceType { get; set; }

    public required string IbanLast4 { get; set; }

    /// <summary>De IBAN op het moment van de run, versleuteld; zo is het bestand later precies opnieuw te maken.</summary>
    public required string IbanProtected { get; set; }

    public required string EndToEndId { get; set; }

    public required string Description { get; set; }
}
