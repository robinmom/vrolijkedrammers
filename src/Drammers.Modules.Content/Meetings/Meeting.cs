using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Content.Meetings;

/// <summary>Voor wie de vergadering is; bepaalt de doelgroep van het agenda-item én wie de agenda per mail krijgt.</summary>
public enum MeetingAudience
{
    Board,
    CouncilOfEleven,
    Both,
}

/// <summary>Namens wie de agenda de deur uit gaat; hetzelfde onderscheid als bij een mailing.</summary>
public enum MeetingSender
{
    Secretary,
    Chairman,
}

/// <summary>
/// Een bestuurs- of raadsvergadering. Datum, tijd, locatie en titel staan in het agenda-item waar de vergadering bij
/// hoort (<see cref="EventId"/>), en de agenda en de notulen zijn bijlagen van dat agenda-item — zo staan ze ook in de
/// app bij de activiteit, voor dezelfde doelgroep. Hier staan alleen de agendapunten en de verzendgeschiedenis.
/// </summary>
public sealed class Meeting : IAuditable
{
    public Guid Id { get; set; }

    public MeetingAudience Audience { get; set; }

    /// <summary>Het agenda-item; één vergadering per agenda-item.</summary>
    public Guid EventId { get; set; }

    /// <summary>Begeleidende tekst boven de agenda in de mail (Markdown); leeg = de standaardtekst.</summary>
    public string? Intro { get; set; }

    public MeetingSender Sender { get; set; }

    public DateTime? AgendaSentAt { get; set; }

    public Guid? AgendaSentBy { get; set; }

    /// <summary>Aantal ontvangers van de laatste verzending.</summary>
    public int AgendaRecipientCount { get; set; }

    public List<MeetingAgendaItem> AgendaItems { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Eén agendapunt; de nummering in de mail volgt <see cref="SortOrder"/>.</summary>
public sealed class MeetingAgendaItem
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public int SortOrder { get; set; }

    public required string Title { get; set; }

    /// <summary>Toelichting onder het punt (bijv. "ter besluitvorming" of wie het inbrengt).</summary>
    public string? Note { get; set; }
}
