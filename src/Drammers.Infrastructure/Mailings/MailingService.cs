using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Notification.Mailing;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Mailings;

/// <summary>Instellingen <c>Mailing:*</c> (fase 27a).</summary>
public sealed class MailingOptions
{
    public const string SectionName = "Mailing";

    /// <summary>
    /// Zoveel mails per uur, zodat we binnen de limiet van Azure Communication Services blijven. Met een eigen domein
    /// standaard 100 per uur (kan bij Azure omhoog); met het domein van Azure zelf 10 per uur, niet te verhogen. Leeg =
    /// 90 met een eigen domein, anders 9. Een grote mailing wordt over de tijd verdeeld.
    /// </summary>
    public int? MaxPerHour { get; set; }

    /// <summary>Of er een eigen afzenderdomein is (<c>Email:CustomSenderDomain</c>); bepaalt het standaardtempo.</summary>
    public bool HasCustomDomain { get; set; }

    public int EffectiveMaxPerHour => Math.Max(1, MaxPerHour ?? (HasCustomDomain ? 90 : 9));

    /// <summary>Openbaar adres van de website (afmeldlink en foto's); standaard <c>Sales:PublicBaseUrl</c>.</summary>
    public string? PublicBaseUrl { get; set; }
}

public sealed record MailingListInput(
    string Name, string? Description, bool AllMembers, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> GroupIds,
    IReadOnlyList<MailingAddressInput> Addresses, bool AllAdvertisers = false);

public sealed record MailingAddressInput(string Email, string? Name);

public sealed record MailingInput(
    MailingKind Kind, string Subject, string? Preheader, IReadOnlyList<MailingBlock> Blocks, IReadOnlyList<Guid> ListIds,
    MailingSender Sender = MailingSender.Secretary);

/// <summary>Een ontvanger zoals hij uit de groepen komt (na het ontdubbelen op e-mailadres).</summary>
public sealed record MailingAddressee(string Email, string? Name, string? FirstName, Guid? MemberId, string? Company = null);

public sealed record MailingAudience(IReadOnlyList<MailingAddressee> Recipients, int Unsubscribed, int WithoutEmail);

/// <summary>Afmeldlinks: het e-mailadres versleuteld, zodat een link niet voor een ander adres te maken is.</summary>
public sealed class MailingUnsubscribeTokens(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Drammers.MailingUnsubscribe.v1");

    public string Create(string email) => _protector.Protect(MailingService.Normalize(email));

    public string? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(token);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>
/// Mailings (fase 27a): mailinggroepen, nieuwsbrieven en uitnodigingen opstellen, een testmail, versturen en afmelden.
/// Versturen gaat via de outbox, één bericht per ontvanger, verdeeld over de tijd (<see cref="MailingOptions.MaxPerHour"/>).
/// Zonder eigen domein in ACS komt de mail van DoNotReply, met het secretariaat als antwoordadres.
/// </summary>
public sealed class MailingService(
    DrammersDbContext db, IOutbox outbox, IAuditLogger audit, IClock clock, ICurrentActor actor, IEmailSender email,
    MailingUnsubscribeTokens tokens, IOptions<MailingOptions> options, IOptions<EmailOptions> emailOptions)
{
    public const string MailMessageType = "mailing.recipient-mail";

    /// <summary>Afzender en antwoordadres: het secretariaat, of de voorzitter (fase 27c). De rol is het deel vóór de @ zodra het eigen domein in ACS is gekoppeld.</summary>
    public static MailingContact ContactFor(MailingSender sender) => sender == MailingSender.Chairman ? MailingContact.Chairman : MailingContact.Secretary;

    public sealed record RecipientMail(long RecipientId);

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static bool IsValidEmail(string? email) =>
        email is { Length: > 3 and <= 254 } && System.Net.Mail.MailAddress.TryCreate(email.Trim(), out var parsed) && parsed.Address == email.Trim();

    // ----- Mailinggroepen ------------------------------------------------------------------------------------------

    public async Task<Guid> CreateListAsync(MailingListInput input, CancellationToken cancellationToken)
    {
        var list = new MailingList { Id = IdGenerator.NewId(), Name = "" };
        db.MailingLists.Add(list);
        await ApplyAsync(list, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("mailing.list-created", "MailingList", list.Id.ToString(), null, Describe(input)), cancellationToken);
        return list.Id;
    }

    public async Task UpdateListAsync(Guid id, MailingListInput input, CancellationToken cancellationToken)
    {
        var list = await db.MailingLists.Include(l => l.Members).Include(l => l.Groups).Include(l => l.Addresses)
            .SingleOrDefaultAsync(l => l.Id == id, cancellationToken) ?? throw ListNotFound();
        await ApplyAsync(list, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("mailing.list-updated", "MailingList", id.ToString(), null, Describe(input)), cancellationToken);
    }

    public async Task DeleteListAsync(Guid id, CancellationToken cancellationToken)
    {
        var list = await db.MailingLists.SingleOrDefaultAsync(l => l.Id == id, cancellationToken) ?? throw ListNotFound();
        if (await db.Mailings.AnyAsync(m => m.Lists.Any(t => t.ListId == id), cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Deze groep is gebruikt in een mailing en kan niet weg. Pas de groep aan of maak een nieuwe.");
        }

        db.MailingLists.Remove(list);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("mailing.list-deleted", "MailingList", id.ToString(), list.Name, null), cancellationToken);
    }

    private async Task ApplyAsync(MailingList list, MailingListInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een naam in van hooguit 100 tekens.");
        }

        var memberIds = input.MemberIds.Distinct().ToList();
        if (await db.Members.CountAsync(m => memberIds.Contains(m.Id), cancellationToken) != memberIds.Count)
        {
            throw new DomainException(ErrorCodes.Validation, "Een of meer leden bestaan niet (meer).");
        }

        var groupIds = input.GroupIds.Distinct().ToList();
        if (await db.Groups.CountAsync(g => groupIds.Contains(g.Id), cancellationToken) != groupIds.Count)
        {
            throw new DomainException(ErrorCodes.Validation, "Een of meer ledengroepen bestaan niet (meer).");
        }

        var invalid = input.Addresses.Where(a => !IsValidEmail(a.Email)).Select(a => a.Email).ToList();
        if (invalid.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, $"Geen geldig e-mailadres: {string.Join(", ", invalid.Take(5))}.");
        }

        (list.Name, list.Description, list.AllMembers, list.AllAdvertisers) =
            (input.Name.Trim(), string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(), input.AllMembers, input.AllAdvertisers);
        list.Members.Clear();
        list.Members.AddRange(memberIds.Select(m => new MailingListMember { ListId = list.Id, MemberId = m }));
        list.Groups.Clear();
        list.Groups.AddRange(groupIds.Select(g => new MailingListGroup { ListId = list.Id, GroupId = g }));
        list.Addresses.Clear();
        list.Addresses.AddRange(input.Addresses.GroupBy(a => Normalize(a.Email)).Select(g => new MailingListAddress
        {
            ListId = list.Id,
            Email = g.Key,
            Name = g.Select(a => a.Name?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n)) is { } name ? name[..Math.Min(name.Length, 150)] : null,
        }));
    }

    /// <summary>
    /// Rekent de ontvangers uit: actieve leden met een geldig e-mailadres (alle leden, losse leden, leden van de
    /// ledengroepen) en de losse adressen; ontdubbeld op e-mailadres en zonder wie zich heeft afgemeld.
    /// </summary>
    public async Task<MailingAudience> ResolveAsync(IReadOnlyCollection<Guid> listIds, CancellationToken cancellationToken)
    {
        var lists = await db.MailingLists.AsNoTracking().Include(l => l.Members).Include(l => l.Groups).Include(l => l.Addresses)
            .Where(l => listIds.Contains(l.Id)).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var members = db.Members.AsNoTracking().Where(m => m.MembershipStatus == MembershipStatus.Active);
        var memberIds = lists.SelectMany(l => l.Members).Select(m => m.MemberId).Distinct().ToList();
        var groupIds = lists.SelectMany(l => l.Groups).Select(g => g.GroupId).Distinct().ToList();
        var groupMembers = db.GroupMemberships.AsNoTracking()
            .Where(gm => groupIds.Contains(gm.GroupId) && (gm.ValidFrom == null || gm.ValidFrom <= today) && (gm.ValidTo == null || gm.ValidTo >= today))
            .Select(gm => gm.MemberId);
        var selected = lists.Any(l => l.AllMembers)
            ? members
            : members.Where(m => memberIds.Contains(m.Id) || groupMembers.Contains(m.Id));
        var people = await selected.Select(m => new { m.Id, m.Email, m.FullName, m.FirstName }).ToListAsync(cancellationToken);

        var unsubscribed = (await db.MailingUnsubscribes.AsNoTracking().Select(u => u.Email).ToListAsync(cancellationToken)).ToHashSet();
        var result = new Dictionary<string, MailingAddressee>();
        int skipped = 0, withoutEmail = 0;
        void Add(string? address, string? name, string? firstName, Guid? memberId, string? company = null)
        {
            if (!IsValidEmail(address))
            {
                withoutEmail++;
                return;
            }

            var key = Normalize(address!);
            if (result.ContainsKey(key))
            {
                return;
            }

            if (unsubscribed.Contains(key))
            {
                skipped++;
                return;
            }

            result[key] = new MailingAddressee(key, name, firstName, memberId, company);
        }

        foreach (var p in people.OrderBy(p => p.FullName))
        {
            Add(p.Email, p.FullName, p.FirstName, p.Id);
        }

        foreach (var a in lists.SelectMany(l => l.Addresses).OrderBy(a => a.Email))
        {
            Add(a.Email, a.Name, null, null);
        }

        // Adverteerders (fase 27c): aanhef op de contactpersoon, anders op de volledige naam van het bedrijf.
        if (lists.Any(l => l.AllAdvertisers))
        {
            var advertisers = await db.Advertisers.AsNoTracking().Where(a => a.Active && a.Email != null).OrderBy(a => a.CompanyName)
                .Select(a => new { a.Email, a.ContactName, a.CompanyName }).ToListAsync(cancellationToken);
            foreach (var a in advertisers)
            {
                Add(a.Email, a.ContactName ?? a.CompanyName, a.ContactName is null ? a.CompanyName : null, null, a.CompanyName);
            }
        }

        return new MailingAudience([.. result.Values], skipped, withoutEmail);
    }

    // ----- Mailings ------------------------------------------------------------------------------------------------

    public async Task<Guid> CreateAsync(MailingInput input, CancellationToken cancellationToken)
    {
        var mailing = new Mailing { Id = IdGenerator.NewId(), Subject = "", Blocks = "[]" };
        db.Mailings.Add(mailing);
        await ApplyAsync(mailing, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("mailing.created", "Mailing", mailing.Id.ToString(), null, mailing.Subject), cancellationToken);
        return mailing.Id;
    }

    public async Task UpdateAsync(Guid id, MailingInput input, CancellationToken cancellationToken)
    {
        var mailing = await DraftAsync(id, cancellationToken);
        await ApplyAsync(mailing, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var mailing = await DraftAsync(id, cancellationToken);
        db.Mailings.Remove(mailing);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("mailing.deleted", "Mailing", id.ToString(), mailing.Subject, null), cancellationToken);
    }

    /// <summary>Een kopie als nieuw concept, bijvoorbeeld voor de uitnodiging van volgend jaar.</summary>
    public async Task<Guid> DuplicateAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await db.Mailings.AsNoTracking().Include(m => m.Lists).SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw MailingNotFound();
        var copy = new Mailing
        {
            Id = IdGenerator.NewId(),
            Kind = source.Kind,
            Sender = source.Sender,
            Subject = source.Subject,
            Preheader = source.Preheader,
            Blocks = source.Blocks,
            Status = MailingStatus.Draft,
        };
        copy.Lists.AddRange(source.Lists.Select(t => new MailingTarget { MailingId = copy.Id, ListId = t.ListId }));
        db.Mailings.Add(copy);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("mailing.created", "Mailing", copy.Id.ToString(), $"kopie van {id}", copy.Subject), cancellationToken);
        return copy.Id;
    }

    private async Task<Mailing> DraftAsync(Guid id, CancellationToken cancellationToken)
    {
        var mailing = await db.Mailings.Include(m => m.Lists).SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw MailingNotFound();
        return mailing.Status == MailingStatus.Draft
            ? mailing
            : throw new DomainException(ErrorCodes.Validation, "Deze mailing is al verstuurd en kan niet meer worden gewijzigd. Maak een kopie.");
    }

    private async Task ApplyAsync(Mailing mailing, MailingInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Subject) || input.Subject.Trim().Length > 200)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een onderwerp in van hooguit 200 tekens.");
        }

        MailingRenderer.Validate(input.Blocks);
        var listIds = input.ListIds.Distinct().ToList();
        if (await db.MailingLists.CountAsync(l => listIds.Contains(l.Id), cancellationToken) != listIds.Count)
        {
            throw new DomainException(ErrorCodes.Validation, "Een of meer mailinggroepen bestaan niet (meer).");
        }

        mailing.Sender = input.Sender;
        (mailing.Kind, mailing.Subject, mailing.Preheader) =
            (input.Kind, input.Subject.Trim(), string.IsNullOrWhiteSpace(input.Preheader) ? null : input.Preheader.Trim()[..Math.Min(input.Preheader.Trim().Length, 200)]);
        mailing.Blocks = JsonSerializer.Serialize(input.Blocks, JsonSerializerOptions.Web);
        mailing.Lists.Clear();
        mailing.Lists.AddRange(listIds.Select(l => new MailingTarget { MailingId = mailing.Id, ListId = l }));
    }

    public static IReadOnlyList<MailingBlock> ReadBlocks(string json) =>
        JsonSerializer.Deserialize<List<MailingBlock>>(json, JsonSerializerOptions.Web) ?? [];

    // ----- Testen en versturen -------------------------------------------------------------------------------------

    /// <summary>Stuurt de mailing (zoals hij nu is opgeslagen) naar de ingelogde gebruiker, met "[TEST]" in het onderwerp.</summary>
    public async Task<string> SendTestAsync(Guid id, CancellationToken cancellationToken)
    {
        var mailing = await db.Mailings.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw MailingNotFound();
        var user = await db.Users.AsNoTracking().Where(u => u.Id == actor.UserId).Select(u => new { u.Email, u.DisplayName, u.MemberId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.Forbidden, "Alleen een ingelogde gebruiker kan een testmail krijgen.");
        var firstName = user.MemberId is { } memberId
            ? await db.Members.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.FirstName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var rendered = Render(mailing, new MailingPerson(firstName, user.DisplayName), user.Email);
        var contact = ContactFor(mailing.Sender);
        await email.SendAsync(new EmailMessage(user.Email, $"[TEST] {rendered.Subject}", rendered.PlainText, rendered.Html, contact.Email, contact.Role), cancellationToken);
        await audit.WriteAsync(new AuditEntry("mailing.test-sent", "Mailing", id.ToString(), null, null), cancellationToken);
        return user.Email;
    }

    /// <summary>
    /// Verstuurt de mailing: legt de ontvangers vast en zet per ontvanger een bericht in de outbox, verdeeld over de tijd.
    /// Geeft het aantal ontvangers en wanneer de laatste mail ongeveer weggaat.
    /// </summary>
    public async Task<(int Recipients, DateTime LastAt)> SendAsync(Guid id, CancellationToken cancellationToken)
    {
        var mailing = await DraftAsync(id, cancellationToken);
        if (string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl))
        {
            // Zonder openbaar adres geen afmeldlink; dan niet versturen.
            throw new DomainException(ErrorCodes.Validation, "Het adres van de website (Sales:PublicBaseUrl) ontbreekt; zonder afmeldlink wordt er niets verstuurd.");
        }

        if (mailing.Lists.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies minstens één mailinggroep.");
        }

        var audience = await ResolveAsync([.. mailing.Lists.Select(t => t.ListId)], cancellationToken);
        if (audience.Recipients.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Deze mailinggroepen hebben geen ontvangers.");
        }

        var now = clock.UtcNow.UtcDateTime;
        var recipients = audience.Recipients.Select(r => new MailingRecipient
        {
            MailingId = id,
            Email = r.Email,
            Name = r.Name?[..Math.Min(r.Name.Length, 150)],
            FirstName = r.FirstName?[..Math.Min(r.FirstName.Length, 100)],
            Company = r.Company?[..Math.Min(r.Company.Length, 200)],
            MemberId = r.MemberId,
        }).ToList();
        db.MailingRecipients.AddRange(recipients);
        (mailing.Status, mailing.SentAt, mailing.SentBy, mailing.RecipientCount) = (MailingStatus.Sending, now, actor.UserId, recipients.Count);
        await db.SaveChangesAsync(cancellationToken);

        var interval = TimeSpan.FromHours(1) / options.Value.EffectiveMaxPerHour;
        var last = now;
        for (var i = 0; i < recipients.Count; i++)
        {
            last = now + (interval * i);
            outbox.Enqueue(MailMessageType, new RecipientMail(recipients[i].Id), i == 0 ? null : last);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("mailing.sent", "Mailing", id.ToString(), null,
            JsonSerializer.Serialize(new { recipients.Count, audience.Unsubscribed, audience.WithoutEmail }, JsonSerializerOptions.Web)), cancellationToken);
        return (recipients.Count, last);
    }

    /// <summary>De mail voor één ontvanger; zonder <paramref name="emailAddress"/> (voorbeeld in het portal) een afmeldlink zonder token.</summary>
    public RenderedMailing Render(Mailing mailing, MailingPerson person, string? emailAddress, Func<string, string?>? imageUrl = null)
    {
        var baseUrl = options.Value.PublicBaseUrl?.TrimEnd('/');
        var unsubscribe = baseUrl is null ? null : emailAddress is null ? $"{baseUrl}/afmelden" : $"{baseUrl}/afmelden?t={Uri.EscapeDataString(tokens.Create(emailAddress))}";
        return MailingRenderer.Render(mailing.Subject, mailing.Preheader, ReadBlocks(mailing.Blocks), person, emailOptions.Value.LogoUrl,
            imageUrl ?? (path => baseUrl is null ? null : $"{baseUrl}{MediaPath(path)}"), unsubscribe, baseUrl, ContactFor(mailing.Sender));
    }

    /// <summary>Openbaar adres van een foto in een mailing (<c>/media/mailing/{id}</c>, zie MediaEndpoints).</summary>
    public static string MediaPath(string uploadPath) => $"/media/mailing/{Path.GetFileNameWithoutExtension(uploadPath)}";

    // ----- Afmelden ------------------------------------------------------------------------------------------------

    /// <summary>Meldt het adres uit de link af; <c>null</c> als de link niet klopt.</summary>
    public async Task<string?> UnsubscribeAsync(string? token, CancellationToken cancellationToken)
    {
        if (tokens.Read(token) is not { } address)
        {
            return null;
        }

        if (await db.MailingUnsubscribes.FindAsync([address], cancellationToken) is null)
        {
            db.MailingUnsubscribes.Add(new MailingUnsubscribe { Email = address, UnsubscribedAt = clock.UtcNow.UtcDateTime });
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("mailing.unsubscribed", "MailingUnsubscribe", "link", null, null), cancellationToken);
        }

        return address;
    }

    /// <summary>Weer aanmelden (op verzoek, door het bestuur).</summary>
    public async Task ResubscribeAsync(string address, CancellationToken cancellationToken)
    {
        var row = await db.MailingUnsubscribes.FindAsync([Normalize(address)], cancellationToken);
        if (row is not null)
        {
            db.MailingUnsubscribes.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("mailing.resubscribed", "MailingUnsubscribe", "portal", null, null), cancellationToken);
        }
    }

    private static string Describe(MailingListInput input) =>
        JsonSerializer.Serialize(new { input.Name, input.AllMembers, input.AllAdvertisers, Members = input.MemberIds.Count, Groups = input.GroupIds.Count, Addresses = input.Addresses.Count },
            JsonSerializerOptions.Web);

    private static DomainException ListNotFound() => new(ErrorCodes.NotFound, "Mailinggroep niet gevonden.", DomainErrorKind.NotFound);

    private static DomainException MailingNotFound() => new(ErrorCodes.NotFound, "Mailing niet gevonden.", DomainErrorKind.NotFound);
}

/// <summary>Verstuurt de mailing naar één ontvanger (outbox); slaat over wie zich intussen heeft afgemeld.</summary>
public sealed class MailingRecipientMailHandler(DrammersDbContext db, MailingService mailings, IEmailSender email, IClock clock) : IOutboxMessageHandler
{
    public string Type => MailingService.MailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var id = JsonSerializer.Deserialize<MailingService.RecipientMail>(message.Payload, JsonSerializerOptions.Web)!.RecipientId;
        var recipient = await db.MailingRecipients.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (recipient is null || recipient.Status != MailingRecipientStatus.Pending)
        {
            return;
        }

        var mailing = await db.Mailings.SingleAsync(m => m.Id == recipient.MailingId, cancellationToken);
        if (await db.MailingUnsubscribes.AnyAsync(u => u.Email == recipient.Email, cancellationToken))
        {
            (recipient.Status, recipient.Error) = (MailingRecipientStatus.Failed, "Afgemeld voor het versturen.");
        }
        else
        {
            try
            {
                var rendered = mailings.Render(mailing, new MailingPerson(recipient.FirstName, recipient.Name, recipient.Company), recipient.Email);
                var contact = MailingService.ContactFor(mailing.Sender);
                await email.SendAsync(
                    new EmailMessage(recipient.Email, rendered.Subject, rendered.PlainText, rendered.Html, contact.Email, contact.Role), cancellationToken);
                (recipient.Status, recipient.SentAt, recipient.Error) = (MailingRecipientStatus.Sent, clock.UtcNow.UtcDateTime, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && message.Attempts + 1 >= OutboxProcessor.MaxAttempts)
            {
                // Laatste poging: als mislukt vastleggen in plaats van eindeloos opnieuw proberen.
                (recipient.Status, recipient.Error) = (MailingRecipientStatus.Failed, ex.Message[..Math.Min(ex.Message.Length, 1000)]);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (!await db.MailingRecipients.AnyAsync(r => r.MailingId == mailing.Id && r.Status == MailingRecipientStatus.Pending, cancellationToken))
        {
            mailing.Status = MailingStatus.Sent;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
