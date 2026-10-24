namespace Drammers.Modules.Membership.Finance;

/// <summary>Het onderdeel waarvan een verzamelfactuur de betaalde transacties bundelt.</summary>
public enum CollectiveInvoiceSource
{
    Advertisers,
    Festival,
    Contribution,
}

/// <summary>
/// Een verzamelfactuur: alle (sinds de vorige) betaalde transacties van één onderdeel in een carnavalsjaar, met een
/// totaal. Door elke factuur te bewaren kan een volgende alleen de nieuw-betaalde regels pakken (delta).
/// </summary>
public sealed class CollectiveInvoice
{
    public Guid Id { get; set; }

    public int CarnivalYearId { get; set; }

    public CollectiveInvoiceSource Source { get; set; }

    /// <summary>Oplopend nummer per carnavalsjaar en onderdeel (VF-2027-ADV-01).</summary>
    public required string Number { get; set; }

    public int Sequence { get; set; }

    public DateOnly InvoiceDate { get; set; }

    public decimal Total { get; set; }

    public int LineCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public List<CollectiveInvoiceLine> Lines { get; set; } = [];
}

/// <summary>Eén betaalde transactie op een verzamelfactuur (bijv. een adverteerder, een clubbetaling of een lid-contributie).</summary>
public sealed class CollectiveInvoiceLine
{
    public Guid Id { get; set; }

    public Guid InvoiceId { get; set; }

    /// <summary>Uniek kenmerk van de bronbetaling, zodat die nooit op twee verzamelfacturen komt (delta).</summary>
    public required string TransactionKey { get; set; }

    public required string Party { get; set; }

    public required string Reference { get; set; }

    public DateOnly PaidOn { get; set; }

    public decimal Amount { get; set; }

    public int SortOrder { get; set; }
}
