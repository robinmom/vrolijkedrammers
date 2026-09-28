namespace Drammers.Modules.Notification.Notifications;

/// <summary>Categorie van een melding (docs/04 §6); bepaalt het Android-kanaal en de voorkeur van de ontvanger.</summary>
public enum NotificationCategory
{
    /// <summary>Dringend: niet uit te zetten, alleen met <c>notification.send.urgent</c>.</summary>
    Urgent,
    Program,
    News,
    Parade,
    DanceGuard,
    Kader,
    Tickets,
    Reminder,

    /// <summary>Systeemmeldingen (account, apparaten); niet uit te zetten.</summary>
    System,
}

public enum NotificationStatus
{
    Scheduled,
    Sending,
    Sent,
    PartiallyFailed,
    Failed,
    Canceled,
}

/// <summary>Een pushmelding met inbox-bericht (fase 10, ADR-009). De doelgroep staat als JSON in <see cref="AudienceJson"/>.</summary>
public sealed class Notification
{
    public const int TitleMaxLength = 65;
    public const int BodyMaxLength = 240;

    public Guid Id { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    public NotificationCategory Category { get; set; }

    /// <summary>Alleen <c>drammers://…</c>; de app valideert het pad opnieuw.</summary>
    public string? DeepLink { get; set; }

    /// <summary><see cref="NotificationAudience"/> als JSON.</summary>
    public required string AudienceJson { get; set; }

    /// <summary>Leeg bij systeemmeldingen.</summary>
    public Guid? SenderUserId { get; set; }

    /// <summary>Bron, bijv. <c>News</c> met het id van het bericht (push bij publicatie).</summary>
    public string? SourceType { get; set; }

    public Guid? SourceId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ScheduledAt { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? CanceledAt { get; set; }

    public Guid? CanceledBy { get; set; }

    public NotificationStatus Status { get; set; }

    /// <summary>Doelgroep uitgerold naar ontvangers (idempotent bij een herstart van de worker).</summary>
    public bool Expanded { get; set; }

    public int RecipientCount { get; set; }

    /// <summary>Pushberichten naar apparaten (een ontvanger kan meerdere apparaten hebben).</summary>
    public int PushCount { get; set; }

    public int DeliveredCount { get; set; }

    public int FailedCount { get; set; }

    public int ReadCount { get; set; }
}

/// <summary>
/// Doelgroep (docs/02 §5.3). <see cref="Everyone"/> = alle accounts plus gasten met push-toestemming;
/// <see cref="Members"/> = iedereen met de rol Lid; anders de vereniging van rollen, groepen en leden. Bij groepen en
/// leden ontvangen ook de ouders/verzorgers ("Namens …").
/// </summary>
public sealed record NotificationAudience(
    bool Everyone = false,
    bool Members = false,
    IReadOnlyList<string>? Roles = null,
    IReadOnlyList<Guid>? Groups = null,
    IReadOnlyList<Guid>? MemberIds = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => !Everyone && !Members && (Roles?.Count ?? 0) == 0 && (Groups?.Count ?? 0) == 0 && (MemberIds?.Count ?? 0) == 0;
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Delivered,
    Failed,

    /// <summary>Geen actief apparaat met push; het bericht staat wel in de inbox.</summary>
    NoDevice,

    /// <summary>Categorie uitgezet; het bericht staat wel in de inbox.</summary>
    OptedOut,
}

/// <summary>Een ontvanger: een account (inbox en gelezen) of een gast-apparaat (alleen push).</summary>
public sealed class NotificationRecipient
{
    public long Id { get; set; }

    public Guid NotificationId { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>Alleen bij gasten zonder account.</summary>
    public Guid? PushDeviceId { get; set; }

    /// <summary>Ouder/verzorger ontvangt namens dit lid (kind).</summary>
    public Guid? OnBehalfOfMemberId { get; set; }

    public DeliveryStatus DeliveryStatus { get; set; }

    public DateTime? ReadAt { get; set; }
}

/// <summary>Eén pushbericht naar één apparaat, met het ticket en de receipt van Expo.</summary>
public sealed class NotificationDelivery
{
    public long Id { get; set; }

    public long RecipientId { get; set; }

    public Guid NotificationId { get; set; }

    public Guid PushDeviceId { get; set; }

    public DeliveryStatus Status { get; set; }

    public string? TicketId { get; set; }

    public string? ErrorCode { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Push-token van een app-installatie (docs/04 §6): van een ingelogd apparaat (<see cref="DeviceId"/>) of van een gast
/// (<see cref="AnonymousInstallId"/>). Het token staat versleuteld opgeslagen (Data Protection); <see cref="TokenHash"/>
/// maakt het uniek en opzoekbaar zonder het te ontsleutelen.
/// </summary>
public sealed class PushDevice
{
    public Guid Id { get; set; }

    public Guid? DeviceId { get; set; }

    public Guid? UserId { get; set; }

    public string? AnonymousInstallId { get; set; }

    public required string Platform { get; set; }

    public required string ProtectedToken { get; set; }

    /// <summary>SHA-256 van het token, hex.</summary>
    public required string TokenHash { get; set; }

    public bool Enabled { get; set; }

    public DateTime LastRegisteredAt { get; set; }

    public DateTime? InvalidatedAt { get; set; }
}

/// <summary>Voorkeur per categorie; ontbreekt de rij, dan staat de categorie aan. Urgent en System zijn niet uit te zetten.</summary>
public sealed class NotificationPreference
{
    public Guid UserId { get; set; }

    public NotificationCategory Category { get; set; }

    public bool Enabled { get; set; }

    public static bool CanDisable(NotificationCategory category) =>
        category is not (NotificationCategory.Urgent or NotificationCategory.System);
}
