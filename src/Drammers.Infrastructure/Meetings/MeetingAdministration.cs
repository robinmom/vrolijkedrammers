using System.Globalization;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Mailings;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Content.Events;
using Drammers.Modules.Content.Meetings;
using Drammers.Modules.Content.Shared;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Meetings;

/// <summary>Een nieuwe vergadering: of bij een bestaand agenda-item, of met een agenda-item dat erbij gemaakt wordt.</summary>
public sealed record MeetingCreateInput(
    MeetingAudience Audience, Guid? EventId, string? Title, DateTime? StartAt, DateTime? EndAt, string? LocationName);

/// <summary>De gegevens van een vergadering; titel, tijd en locatie gaan naar het agenda-item.</summary>
public sealed record MeetingDetailsInput(
    MeetingAudience Audience, string Title, DateTime StartAt, DateTime? EndAt, string? LocationName, string? Intro, MeetingSender Sender);

public sealed record MeetingAgendaItemInput(string Title, string? Note);

public sealed record MeetingSummary(
    Guid Id, Guid EventId, MeetingAudience Audience, string Title, DateTime StartAt, string? LocationName, int AgendaItems, int Documents,
    DateTime? AgendaSentAt, int AgendaRecipientCount);

public sealed record MeetingAgendaItemView(Guid Id, string Title, string? Note);

public sealed record MeetingView(
    Guid Id, Guid EventId, MeetingAudience Audience, string Title, DateTime StartAt, DateTime? EndAt, string? LocationName, string? Intro,
    MeetingSender Sender, DateTime? AgendaSentAt, int AgendaRecipientCount, IReadOnlyList<MeetingAgendaItemView> AgendaItems);

/// <summary>Eén ontvanger van de agendamail.</summary>
public sealed record MeetingRecipient(string Email, string Name, string RoleName);

/// <summary>Wie de agenda krijgt, en wie hem mist omdat er geen e-mailadres bekend is.</summary>
public sealed record MeetingAudienceView(IReadOnlyList<MeetingRecipient> Recipients, IReadOnlyList<string> WithoutEmail);

public sealed record MeetingSendResult(int Recipients, DateTime LastAt);

/// <summary>
/// Vergaderingen van het bestuur en de raad van elf (2026-11-16). Een vergadering hangt aan een agenda-item: daar staan
/// de datum, de tijd en de locatie, en daar hangen de agenda en de notulen als bijlage, zodat ze ook in de app bij de
/// activiteit staan. Hier worden de agendapunten bijgehouden en gaat de agenda in de huisstijl de deur uit, namens de
/// secretaris of de voorzitter, naar iedereen met de rol Bestuur en/of Raad van Elf.
/// </summary>
public sealed class MeetingAdministration(
    DrammersDbContext db,
    ContentAdministration content,
    IFileStore files,
    IOutbox outbox,
    IAuditLogger audit,
    ICurrentActor actor,
    IClock clock,
    IOptions<EmailOptions> emailOptions,
    IOptions<MailingOptions> mailingOptions)
{
    public const string AgendaMailMessageType = "meeting.agenda-mail";

    /// <summary>Code van de agenda-categorie waar nieuwe vergaderingen in komen; wordt gemaakt als hij nog niet bestaat.</summary>
    public const string CategoryCode = "vergadering";

    /// <summary>
    /// Afstand tussen twee agendamails. Exchange Online neemt ongeveer dertig berichten per minuut aan; met drie
    /// seconden blijft een vergadering van dertig mensen binnen twee minuten rond, zonder die grens te raken.
    /// </summary>
    public static readonly TimeSpan MailInterval = TimeSpan.FromSeconds(3);

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    /// <summary>Het soort vergadering in woorden, voor de mail en het portal.</summary>
    public static string AudienceName(MeetingAudience audience) => audience switch
    {
        MeetingAudience.Board => "bestuursvergadering",
        MeetingAudience.CouncilOfEleven => "vergadering van de raad van elf",
        _ => "gezamenlijke vergadering van bestuur en raad van elf",
    };

    /// <summary>De rollen die de agenda krijgen en de doelgroep van het agenda-item vormen.</summary>
    public static IReadOnlyList<string> RolesFor(MeetingAudience audience) => audience switch
    {
        MeetingAudience.Board => [DefaultRoles.Bestuur],
        MeetingAudience.CouncilOfEleven => [DefaultRoles.RaadVanElf],
        _ => [DefaultRoles.Bestuur, DefaultRoles.RaadVanElf],
    };

    public async Task<IReadOnlyList<MeetingSummary>> ListAsync(MeetingAudience? audience, CancellationToken cancellationToken) =>
        await (from m in db.Meetings.AsNoTracking().Where(m => audience == null || m.Audience == audience)
               join e in db.Events.AsNoTracking() on m.EventId equals e.Id
               orderby e.StartAt descending
               select new MeetingSummary(m.Id, m.EventId, m.Audience, e.Title, e.StartAt, e.LocationName,
                   db.MeetingAgendaItems.Count(a => a.MeetingId == m.Id), db.EventAttachments.Count(a => a.EventId == m.EventId),
                   m.AgendaSentAt, m.AgendaRecipientCount))
            .ToListAsync(cancellationToken);

    public async Task<MeetingView> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var (meeting, agendaEvent) = await FindAsync(id, cancellationToken);
        var items = await ItemsAsync(id, cancellationToken);
        return new MeetingView(meeting.Id, meeting.EventId, meeting.Audience, agendaEvent.Title, agendaEvent.StartAt, agendaEvent.EndAt,
            agendaEvent.LocationName, meeting.Intro, meeting.Sender, meeting.AgendaSentAt, meeting.AgendaRecipientCount,
            [.. items.Select(a => new MeetingAgendaItemView(a.Id, a.Title, a.Note))]);
    }

    /// <summary>
    /// Maakt de vergadering. Zonder <see cref="MeetingCreateInput.EventId"/> komt er een agenda-item bij met de juiste
    /// doelgroep (bestuur en/of raad van elf), zodat de vergadering meteen in de agenda van de app staat.
    /// </summary>
    public async Task<Guid> CreateAsync(MeetingCreateInput input, CancellationToken cancellationToken)
    {
        var eventId = input.EventId ?? await CreateEventAsync(input, cancellationToken);
        if (input.EventId is { } linked)
        {
            if (!await db.Events.AnyAsync(e => e.Id == linked, cancellationToken))
            {
                throw new DomainException(ErrorCodes.ContentNotFound, "Dat agenda-item bestaat niet.", DomainErrorKind.NotFound);
            }

            if (await db.Meetings.AnyAsync(m => m.EventId == linked, cancellationToken))
            {
                throw new DomainException(ErrorCodes.Validation, "Bij dat agenda-item hoort al een vergadering.", DomainErrorKind.Conflict);
            }
        }

        var meeting = new Meeting { Id = IdGenerator.NewId(), Audience = input.Audience, EventId = eventId };
        db.Meetings.Add(meeting);
        await SaveWithAuditAsync("meeting.created", meeting, new { meeting.Audience, meeting.EventId, Linked = input.EventId is not null }, cancellationToken);
        return meeting.Id;
    }

    public async Task UpdateAsync(Guid id, MeetingDetailsInput input, CancellationToken cancellationToken)
    {
        var (meeting, agendaEvent) = await FindAsync(id, cancellationToken, track: true);
        var title = Clean(input.Title, 200, "Geef de vergadering een titel (maximaal 200 tekens).");
        meeting.Audience = input.Audience;
        meeting.Sender = input.Sender;
        meeting.Intro = Trim(input.Intro, 2000);
        // Titel, tijd en locatie horen bij het agenda-item: dat is de agenda van de app en de bron voor de mail.
        (agendaEvent.Title, agendaEvent.StartAt, agendaEvent.EndAt, agendaEvent.LocationName) =
            (title, input.StartAt, input.EndAt, Trim(input.LocationName, 200));
        await SetAudienceAsync(agendaEvent, input.Audience, cancellationToken);
        await SaveWithAuditAsync("meeting.updated", meeting, new { input.Audience, Title = title, input.StartAt, input.LocationName }, cancellationToken);
    }

    /// <summary>Haalt de vergadering weg; het agenda-item met de bijlagen blijft in de agenda staan.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var (meeting, _) = await FindAsync(id, cancellationToken, track: true);
        db.Meetings.Remove(meeting);
        await SaveWithAuditAsync("meeting.deleted", meeting, new { meeting.EventId }, cancellationToken);
    }

    /// <summary>Zet de agendapunten in deze volgorde; wat er niet in staat verdwijnt.</summary>
    public async Task SaveAgendaAsync(Guid id, IReadOnlyList<MeetingAgendaItemInput> items, CancellationToken cancellationToken)
    {
        var (meeting, _) = await FindAsync(id, cancellationToken, track: true);
        if (items.Count > 60)
        {
            throw new DomainException(ErrorCodes.Validation, "Een agenda heeft hooguit 60 punten.");
        }

        var existing = await db.MeetingAgendaItems.Where(a => a.MeetingId == id).ToListAsync(cancellationToken);
        db.MeetingAgendaItems.RemoveRange(existing);
        for (var i = 0; i < items.Count; i++)
        {
            db.MeetingAgendaItems.Add(new MeetingAgendaItem
            {
                Id = IdGenerator.NewId(),
                MeetingId = id,
                SortOrder = i + 1,
                Title = Clean(items[i].Title, 200, $"Agendapunt {i + 1}: vul een titel in (maximaal 200 tekens)."),
                Note = Trim(items[i].Note, 1000),
            });
        }

        await SaveWithAuditAsync("meeting.agenda-saved", meeting, new { Items = items.Count }, cancellationToken);
    }

    /// <summary>
    /// Neemt de agendapunten over van de vorige vergadering van dezelfde soort (de laatste die eerder was). Handig omdat
    /// een vaste agenda meestal voor negentig procent hetzelfde is; geeft het aantal overgenomen punten.
    /// </summary>
    public async Task<int> CopyAgendaFromPreviousAsync(Guid id, CancellationToken cancellationToken)
    {
        var (meeting, agendaEvent) = await FindAsync(id, cancellationToken, track: true);
        var previous = await (from m in db.Meetings.AsNoTracking().Where(m => m.Id != id && m.Audience == meeting.Audience)
                              join e in db.Events.AsNoTracking() on m.EventId equals e.Id
                              where e.StartAt < agendaEvent.StartAt
                              orderby e.StartAt descending
                              select m.Id).FirstOrDefaultAsync(cancellationToken);
        if (previous == Guid.Empty)
        {
            throw new DomainException(ErrorCodes.Validation, $"Er is geen eerdere {AudienceName(meeting.Audience)} om de agenda van over te nemen.");
        }

        var source = await ItemsAsync(previous, cancellationToken);
        if (source.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "De vorige vergadering heeft geen agendapunten.");
        }

        await SaveAgendaAsync(id, [.. source.Select(a => new MeetingAgendaItemInput(a.Title, a.Note))], cancellationToken);
        return source.Count;
    }

    /// <summary>Iedereen met de rol Bestuur en/of Raad van Elf, plus de losse adressen die erbij getypt zijn.</summary>
    public async Task<MeetingAudienceView> RecipientsAsync(Guid id, IReadOnlyCollection<string>? extraEmails, CancellationToken cancellationToken)
    {
        var (meeting, _) = await FindAsync(id, cancellationToken);
        var codes = RolesFor(meeting.Audience);
        var roles = await db.Roles.AsNoTracking().Where(r => codes.Contains(r.Code)).Select(r => new { r.Id, r.Name }).ToListAsync(cancellationToken);
        var roleIds = roles.Select(r => r.Id).ToList();
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var users = await db.Users.AsNoTracking()
            .Where(u => u.AccountStatus == AccountStatus.Active
                && u.Roles.Any(r => roleIds.Contains(r.RoleId)
                    && (r.ValidFrom == null || r.ValidFrom <= today) && (r.ValidTo == null || r.ValidTo >= today)))
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.DisplayName,
                u.Email,
                Roles = u.Roles.Where(r => roleIds.Contains(r.RoleId)).Select(r => r.RoleId).ToList(),
            })
            .ToListAsync(cancellationToken);

        var recipients = new List<MeetingRecipient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var withoutEmail = new List<string>();
        foreach (var user in users)
        {
            var roleName = string.Join(" en ", roles.Where(r => user.Roles.Contains(r.Id)).Select(r => r.Name));
            if (!IsEmail(user.Email))
            {
                withoutEmail.Add(user.DisplayName);
                continue;
            }

            if (seen.Add(user.Email))
            {
                recipients.Add(new MeetingRecipient(user.Email, user.DisplayName, roleName));
            }
        }

        foreach (var extra in extraEmails ?? [])
        {
            var address = extra.Trim();
            if (!IsEmail(address))
            {
                throw new DomainException(ErrorCodes.Validation, $"\"{extra}\" is geen e-mailadres.");
            }

            if (seen.Add(address))
            {
                recipients.Add(new MeetingRecipient(address, address, "los adres"));
            }
        }

        return new MeetingAudienceView(recipients, withoutEmail);
    }

    /// <summary>De agenda zoals hij eruitziet in de mail; voor het voorbeeld in het portal.</summary>
    public async Task<RenderedMailing> RenderAsync(Guid id, IReadOnlyCollection<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        var (meeting, agendaEvent) = await FindAsync(id, cancellationToken);
        var items = await ItemsAsync(id, cancellationToken);
        var names = await AttachmentNamesAsync(meeting.EventId, attachmentIds, cancellationToken);
        return Render(meeting, agendaEvent, items, names, new MailingPerson(null, "Piet van der Drammer"), mailingOptions.Value.PublicBaseUrl,
            emailOptions.Value.LogoUrl);
    }

    /// <summary>
    /// Zet de agendamail voor elke ontvanger in de outbox, zodat het verzoek van het portal niet op de mailserver wacht
    /// en een hapering later opnieuw geprobeerd wordt.
    /// </summary>
    public async Task<MeetingSendResult> SendAgendaAsync(
        Guid id, IReadOnlyCollection<Guid> attachmentIds, IReadOnlyCollection<string>? extraEmails, CancellationToken cancellationToken)
    {
        var (meeting, _) = await FindAsync(id, cancellationToken, track: true);
        if ((await ItemsAsync(id, cancellationToken)).Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Zet eerst agendapunten op de agenda.");
        }

        var attachments = (await AttachmentNamesAsync(meeting.EventId, attachmentIds, cancellationToken)).Select(a => a.Id).ToList();
        var audience = await RecipientsAsync(id, extraEmails, cancellationToken);
        if (audience.Recipients.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Niemand met deze rol heeft een e-mailadres; voeg een los adres toe.");
        }

        var now = clock.UtcNow.UtcDateTime;
        var last = now;
        for (var i = 0; i < audience.Recipients.Count; i++)
        {
            var recipient = audience.Recipients[i];
            last = now + (MailInterval * i);
            outbox.Enqueue(AgendaMailMessageType, new AgendaMail(id, recipient.Email, recipient.Name, [.. attachments]), i == 0 ? null : last);
        }

        (meeting.AgendaSentAt, meeting.AgendaSentBy, meeting.AgendaRecipientCount) = (now, actor.UserId, audience.Recipients.Count);
        var result = new MeetingSendResult(audience.Recipients.Count, last);
        await SaveWithAuditAsync("meeting.agenda-sent", meeting,
            new { result.Recipients, Attachments = attachments.Count, Sender = meeting.Sender.ToString() }, cancellationToken);
        return result;
    }

    /// <summary>Eén agendamail uit de outbox; de bijlagen worden pas bij het versturen uit de opslag gehaald.</summary>
    public sealed record AgendaMail(Guid MeetingId, string Email, string Name, Guid[] AttachmentIds);

    /// <summary>De mail in de huisstijl: kop, begeleidende tekst, wanneer en waar, de genummerde agenda en de groet.</summary>
    internal static RenderedMailing Render(
        Meeting meeting, Event agendaEvent, IReadOnlyList<MeetingAgendaItem> items, IReadOnlyList<MeetingAttachment> attachments,
        MailingPerson person, string? baseUrl, string? logoUrl)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(agendaEvent.StartAt, DateTimeKind.Utc), Amsterdam);
        var intro = string.IsNullOrWhiteSpace(meeting.Intro)
            ? $"Hierbij de agenda voor de {AudienceName(meeting.Audience)}."
            : meeting.Intro!.Trim();
        var blocks = new List<MailingBlock>
        {
            new("heading", agendaEvent.Title),
            new("text", $"Beste {{voornaam}},\n\n{intro}"),
            new("highlight", local.ToString("dddd d MMMM yyyy 'om' HH:mm", Dutch), "Wanneer", Note: agendaEvent.LocationName),
            new("text", AgendaMarkdown(items)),
        };

        if (attachments.Count > 0)
        {
            var names = string.Join(", ", attachments.Select(a => a.FileName));
            blocks.Add(new MailingBlock("text", attachments.Count == 1 ? $"Als bijlage: {names}." : $"Als bijlagen: {names}."));
        }

        var contact = meeting.Sender == MeetingSender.Chairman ? MailingContact.Chairman : MailingContact.Secretary;
        var signature = meeting.Sender == MeetingSender.Chairman ? "Voorzitter" : "Secretaris";
        blocks.Add(new MailingBlock("closing", $"Met vriendelijke groet,\n{signature} De Vrolijke Drammers"));
        var subject = $"Agenda {agendaEvent.Title} – {local.ToString("d MMMM yyyy", Dutch)}";
        return MailingRenderer.Render(subject, null, blocks, person, logoUrl, _ => null, null, baseUrl, contact);
    }

    /// <summary>De agenda als genummerde lijst; de toelichting komt schuin onder het punt.</summary>
    private static string AgendaMarkdown(IReadOnlyList<MeetingAgendaItem> items)
    {
        var markdown = new StringBuilder("## Agenda\n\n");
        foreach (var item in items.OrderBy(a => a.SortOrder))
        {
            markdown.Append(item.SortOrder.ToString(CultureInfo.InvariantCulture)).Append(". ").Append(item.Title).Append('\n');
            if (item.Note is { Length: > 0 } note)
            {
                // Vier spaties: zo hoort de toelichting in Markdown bij het punt erboven.
                markdown.Append("    ").Append(note.Replace("\n", " ", StringComparison.Ordinal)).Append('\n');
            }
        }

        return markdown.ToString();
    }

    /// <summary>
    /// De gekozen documenten van de vergadering. Bij <paramref name="strict"/> moet elk gekozen document bestaan (zo
    /// merkt het portal een vergissing meteen); de verzendhandler is juist mild, want een document dat ondertussen is
    /// weggehaald mag de agendamail niet blijven blokkeren.
    /// </summary>
    internal async Task<IReadOnlyList<MeetingAttachment>> AttachmentNamesAsync(
        Guid eventId, IReadOnlyCollection<Guid> attachmentIds, CancellationToken cancellationToken, bool strict = true)
    {
        if (attachmentIds.Count == 0)
        {
            return [];
        }

        var attachments = await db.EventAttachments.AsNoTracking()
            .Where(a => a.EventId == eventId && attachmentIds.Contains(a.Id)).OrderBy(a => a.FileName)
            .Select(a => new MeetingAttachment(a.Id, a.FileName, a.ContentType, a.BlobPath, a.SizeBytes))
            .ToListAsync(cancellationToken);
        if (strict && attachments.Count != attachmentIds.Distinct().Count())
        {
            throw new DomainException(ErrorCodes.Validation, "Niet alle gekozen documenten horen bij deze vergadering.");
        }

        return attachments;
    }

    /// <summary>Haalt de bijlage uit de opslag; <c>null</c> als hij ondertussen verwijderd is.</summary>
    internal async Task<EmailFile?> ReadAttachmentAsync(MeetingAttachment attachment, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await files.OpenReadAsync(FileContainers.Content, attachment.BlobPath, cancellationToken);
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return new EmailFile(attachment.FileName, attachment.ContentType, buffer.ToArray());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<Guid> CreateEventAsync(MeetingCreateInput input, CancellationToken cancellationToken)
    {
        var title = Clean(input.Title, 200, "Geef de vergadering een titel (maximaal 200 tekens).");
        if (input.StartAt is not { } startAt)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul de datum en tijd van de vergadering in.");
        }

        var categoryId = await CategoryIdAsync(cancellationToken);
        var publication = new PublicationInput(ContentVisibility.Restricted, RolesFor(input.Audience), PublicationStatus.Published, null);
        return await content.CreateEventAsync(
            new EventInput(categoryId, title, null, null, startAt, input.EndAt, false, Trim(input.LocationName, 200), null, null, null, false, null, publication),
            cancellationToken);
    }

    /// <summary>De categorie "Vergadering"; die wordt één keer aangemaakt en daarna hergebruikt.</summary>
    private async Task<int> CategoryIdAsync(CancellationToken cancellationToken)
    {
        if (await db.EventCategories.AsNoTracking().Where(c => c.Code == CategoryCode).Select(c => (int?)c.Id).FirstOrDefaultAsync(cancellationToken) is { } id)
        {
            return id;
        }

        return await content.SaveEventCategoryAsync(null, "Vergadering", 50, cancellationToken);
    }

    /// <summary>Het agenda-item is alleen voor de betrokken rollen; zo staat een vergadering nooit op de website.</summary>
    private async Task SetAudienceAsync(Event agendaEvent, MeetingAudience audience, CancellationToken cancellationToken)
    {
        await db.Entry(agendaEvent).Collection(e => e.Audiences).LoadAsync(cancellationToken);
        agendaEvent.Visibility = ContentVisibility.Restricted;
        agendaEvent.Audiences.RemoveAll(a => a.AudienceType == AudienceType.Role);
        foreach (var role in RolesFor(audience))
        {
            agendaEvent.Audiences.Add(new EventAudience { EventId = agendaEvent.Id, AudienceType = AudienceType.Role, AudienceRef = role });
        }
    }

    private async Task<(Meeting Meeting, Event Event)> FindAsync(Guid id, CancellationToken cancellationToken, bool track = false)
    {
        var meetings = track ? db.Meetings : db.Meetings.AsNoTracking();
        var meeting = await meetings.SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ContentNotFound, "Vergadering niet gevonden.", DomainErrorKind.NotFound);
        var events = track ? db.Events : db.Events.AsNoTracking();
        var agendaEvent = await events.SingleOrDefaultAsync(e => e.Id == meeting.EventId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ContentNotFound, "Het agenda-item van deze vergadering bestaat niet meer.", DomainErrorKind.NotFound);
        return (meeting, agendaEvent);
    }

    private async Task<List<MeetingAgendaItem>> ItemsAsync(Guid id, CancellationToken cancellationToken) =>
        await db.MeetingAgendaItems.AsNoTracking().Where(a => a.MeetingId == id).OrderBy(a => a.SortOrder).ToListAsync(cancellationToken);

    private async Task SaveWithAuditAsync(string action, Meeting meeting, object values, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry(action, "Meeting", meeting.Id.ToString(), null, JsonSerializer.Serialize(values, Json)), cancellationToken);
    }

    private static bool IsEmail(string? address) => !string.IsNullOrWhiteSpace(address) && System.Net.Mail.MailAddress.TryCreate(address, out _);

    private static string Clean(string? value, int max, string message) =>
        value?.Trim() is { Length: > 0 } clean && clean.Length <= max ? clean : throw new DomainException(ErrorCodes.Validation, message);

    private static string? Trim(string? value, int max) =>
        value?.Trim() is { Length: > 0 } clean ? clean[..Math.Min(clean.Length, max)] : null;
}

/// <summary>Een document van de vergadering: een bijlage van het agenda-item.</summary>
public sealed record MeetingAttachment(Guid Id, string FileName, string ContentType, string BlobPath, long SizeBytes);

/// <summary>
/// Verstuurt één agendamail uit de outbox, in de huisstijl en namens de secretaris of de voorzitter. Gaat via de
/// mailbox in Microsoft 365 (<c>ViaMailbox</c>), zodat de mail bij Verzonden items staat en niet uit de ACS-pot van
/// honderd mails per uur komt. De bijlagen worden hier pas uit de opslag gehaald, zodat ze niet in de outbox staan.
/// </summary>
public sealed class MeetingAgendaMailHandler(
    DrammersDbContext db,
    MeetingAdministration meetings,
    IEmailSender email,
    IOptions<EmailOptions> emailOptions,
    IOptions<MailingOptions> mailingOptions) : IOutboxMessageHandler
{
    public string Type => MeetingAdministration.AgendaMailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var mail = JsonSerializer.Deserialize<MeetingAdministration.AgendaMail>(message.Payload, JsonSerializerOptions.Web)!;
        var meeting = await db.Meetings.AsNoTracking().SingleOrDefaultAsync(m => m.Id == mail.MeetingId, cancellationToken);
        var agendaEvent = meeting is null
            ? null
            : await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == meeting.EventId, cancellationToken);
        if (meeting is null || agendaEvent is null)
        {
            // Vergadering of agenda-item is ondertussen verwijderd: niets te versturen.
            return;
        }

        var items = await db.MeetingAgendaItems.AsNoTracking().Where(a => a.MeetingId == meeting.Id).OrderBy(a => a.SortOrder)
            .ToListAsync(cancellationToken);
        var chosen = await meetings.AttachmentNamesAsync(meeting.EventId, mail.AttachmentIds, cancellationToken, strict: false);
        var files = new List<EmailFile>();
        foreach (var attachment in chosen)
        {
            if (await meetings.ReadAttachmentAsync(attachment, cancellationToken) is { } file)
            {
                files.Add(file);
            }
        }

        var contact = meeting.Sender == MeetingSender.Chairman ? MailingContact.Chairman : MailingContact.Secretary;
        var rendered = MeetingAdministration.Render(meeting, agendaEvent, items, [.. chosen], new MailingPerson(null, mail.Name),
            mailingOptions.Value.PublicBaseUrl, emailOptions.Value.LogoUrl);
        await email.SendAsync(
            new EmailMessage(mail.Email, rendered.Subject, rendered.PlainText, rendered.Html, contact.Email, contact.Role, files, ViaMailbox: true),
            cancellationToken);
    }
}
