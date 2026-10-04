using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Notification.Mailing;

/// <summary>Soort mailing; beide houden rekening met afmeldingen.</summary>
public enum MailingKind
{
    Newsletter,
    Invitation,
}

public enum MailingStatus
{
    Draft,
    Sending,
    Sent,
}

public enum MailingRecipientStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>
/// Mailinggroep (fase 27a): alle actieve leden, losse leden, ledengroepen (bijvoorbeeld Raad van Elf) en losse
/// e-mailadressen van externen. Bij het versturen wordt de groep pas uitgerekend, zodat hij altijd actueel is.
/// </summary>
public sealed class MailingList : IAuditable
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Alle actieve leden met een e-mailadres (de nieuwsbrief).</summary>
    public bool AllMembers { get; set; }

    public List<MailingListMember> Members { get; set; } = [];

    public List<MailingListGroup> Groups { get; set; } = [];

    public List<MailingListAddress> Addresses { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

public sealed class MailingListMember
{
    public Guid ListId { get; set; }

    public Guid MemberId { get; set; }
}

public sealed class MailingListGroup
{
    public Guid ListId { get; set; }

    public Guid GroupId { get; set; }
}

/// <summary>Los e-mailadres (bijvoorbeeld een gast van de pronkzitting); in kleine letters opgeslagen.</summary>
public sealed class MailingListAddress
{
    public Guid ListId { get; set; }

    public required string Email { get; set; }

    public string? Name { get; set; }
}

/// <summary>
/// Een nieuwsbrief of uitnodiging (fase 27a), opgebouwd uit blokken (kop, tekst, foto, knop, uitgelicht, lijn) en
/// verstuurd in de huisstijl naar een of meer mailinggroepen.
/// </summary>
public sealed class Mailing : IAuditable
{
    public Guid Id { get; set; }

    public MailingKind Kind { get; set; }

    public required string Subject { get; set; }

    /// <summary>Korte tekst die mailprogramma's naast het onderwerp tonen.</summary>
    public string? Preheader { get; set; }

    /// <summary>De blokken als JSON.</summary>
    public required string Blocks { get; set; }

    public MailingStatus Status { get; set; }

    public DateTime? SentAt { get; set; }

    public Guid? SentBy { get; set; }

    public int RecipientCount { get; set; }

    public List<MailingTarget> Lists { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

public sealed class MailingTarget
{
    public Guid MailingId { get; set; }

    public Guid ListId { get; set; }
}

/// <summary>Eén ontvanger van een verstuurde mailing; vastgelegd bij het versturen, zodat niemand de mail twee keer krijgt.</summary>
public sealed class MailingRecipient
{
    public long Id { get; set; }

    public Guid MailingId { get; set; }

    public required string Email { get; set; }

    public string? Name { get; set; }

    public string? FirstName { get; set; }

    public Guid? MemberId { get; set; }

    public MailingRecipientStatus Status { get; set; }

    public DateTime? SentAt { get; set; }

    public string? Error { get; set; }
}

/// <summary>Wie zich heeft afgemeld, krijgt geen nieuwsbrieven en uitnodigingen meer (verenigingsberichten wel).</summary>
public sealed class MailingUnsubscribe
{
    /// <summary>In kleine letters.</summary>
    public required string Email { get; set; }

    public DateTime UnsubscribedAt { get; set; }
}
