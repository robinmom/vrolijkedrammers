namespace Drammers.Modules.Ticketing.Sales;

public enum SaleChannel
{
    App,
    Web,
    Portal,
}

public enum SaleOrderStatus
{
    /// <summary>Wacht op de betaling; de plaatsen worden zolang vastgehouden (<see cref="SaleOrder.HoldUntil"/>).</summary>
    AwaitingPayment,

    /// <summary>Betaald of gratis: de kaarten (QR) zijn uitgegeven.</summary>
    Confirmed,

    /// <summary>Door het bestuur geannuleerd; nooit terugbetaald (de QR vervalt).</summary>
    Cancelled,

    /// <summary>Niet op tijd betaald; de plaatsen zijn weer vrij.</summary>
    Expired,
}

public enum SalePaymentMethod
{
    /// <summary>Alleen gratis groepskaarten voor leden.</summary>
    Free,

    /// <summary>Via Mollie (iDEAL e.d.), ook via een betaallink per e-mail.</summary>
    Mollie,

    /// <summary>Contant ontvangen; alleen te boeken in het portal.</summary>
    Cash,
}

/// <summary>
/// Een bestelling (fase 19) van één product. Bij de pronkzitting kan een lid gratis kaarten voor de eigen groep
/// bestellen (<see cref="MemberQuantity"/>, groep uit e-Boekhouden vrij veld 3) plus betaalde losse kaarten
/// (<see cref="PaidQuantity"/>). Wie niet ingelogd is, bestelt alleen betaalde kaarten. Nooit terugbetalen.
/// </summary>
public sealed class SaleOrder
{
    public Guid Id { get; set; }

    /// <summary>Bijv. <c>2027-0142</c>: jaar van carnaval en een volgnummer.</summary>
    public required string Number { get; set; }

    public int CarnivalYearId { get; set; }

    public Guid ProductId { get; set; }

    public SaleChannel Channel { get; set; }

    public SaleOrderStatus Status { get; set; }

    public SalePaymentMethod PaymentMethod { get; set; }

    /// <summary>De groep waarvoor een lid gratis kaarten bestelde.</summary>
    public string? GroupName { get; set; }

    public int MemberQuantity { get; set; }

    public int PaidQuantity { get; set; }

    public int AmountCents { get; set; }

    public required string BuyerName { get; set; }

    public required string BuyerEmail { get; set; }

    public string? BuyerPhone { get; set; }

    /// <summary>Opmerking of wensen (bijv. voor de tafelindeling).</summary>
    public string? Remark { get; set; }

    public Guid? BuyerUserId { get; set; }

    public Guid? BuyerMemberId { get; set; }

    /// <summary>
    /// Het geheime token waarmee een gast de bestelling en QR opent, versleuteld (Data Protection): zo kunnen latere
    /// e-mails (bevestiging, betaallink) dezelfde link sturen die de koper al heeft.
    /// </summary>
    public required string AccessTokenProtected { get; set; }

    /// <summary>De laatste Mollie-betaling (<c>tr_…</c>).</summary>
    public string? MolliePaymentId { get; set; }

    /// <summary>Tot wanneer de plaatsen voor een onbetaalde bestelling vastgehouden worden.</summary>
    public DateTime? HoldUntil { get; set; }

    public DateTime? PaidAt { get; set; }

    public Guid? WaitlistEntryId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Wie de bestelling in het portal invoerde.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// Munten: het toestel waarop ze in de app gekocht zijn. De munten-QR werkt alleen op dat toestel en is nooit over te
    /// zetten (er zit een betaling achter).
    /// </summary>
    public Guid? PurchaseDeviceId { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancelReason { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public int Quantity => MemberQuantity + PaidQuantity;
}

public enum OrderTicketStatus
{
    Active,

    /// <summary>Gescand bij de ingang (alle personen tegelijk) of munten uitgegeven.</summary>
    Used,

    Cancelled,
}

/// <summary>
/// De QR bij een bestelling: één code voor alle kaarten (<see cref="Quantity"/> personen). Een lid kan kaarten delen
/// met een lid van dezelfde groep; die krijgen een eigen <see cref="OrderTicket"/> (fase 19b). De QR bevat alleen
/// <see cref="PublicRef"/> (128 bit, CSPRNG), geen persoonsgegevens.
/// </summary>
public sealed class OrderTicket
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public byte[] PublicRef { get; set; } = [];

    public int Quantity { get; set; }

    /// <summary>Het lid dat deze kaarten heeft (de besteller, of een groepslid met wie gedeeld is).</summary>
    public Guid? HolderMemberId { get; set; }

    public Guid? SharedFromTicketId { get; set; }

    public OrderTicketStatus Status { get; set; }

    public DateTime? UsedAt { get; set; }

    /// <summary>
    /// Munten: het enige toestel waarop de munten-QR werkt (het toestel van de aankoop). Wordt eenmalig gezet en nooit
    /// gewijzigd; <see cref="BoundAt"/> blijft staan, ook als het toestel verdwijnt, zodat er nooit opnieuw gekoppeld wordt.
    /// </summary>
    public Guid? BoundDeviceId { get; set; }

    public DateTime? BoundAt { get; set; }

    /// <summary>
    /// Munten: het bestuur heeft ze (bijv. bij een kapotte telefoon) één keer naar een ander toestel van het lid verplaatst.
    /// Daarna kan dat nooit meer.
    /// </summary>
    public DateTime? MovedAt { get; set; }

    public Guid? MovedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Volgnummer van bestellingen per carnavalsjaar (zoals het opgavenummer van de optocht).</summary>
public sealed class SaleOrderSequence
{
    public int CarnivalYearId { get; set; }

    public int LastNumber { get; set; }
}
