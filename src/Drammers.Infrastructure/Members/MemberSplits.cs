using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Mailings;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Applications;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Members;

public enum SplitState
{
    /// <summary>Nog niet gemaild.</summary>
    NotInvited,

    /// <summary>In de wachtrij: de uitnodiging staat in de outbox, de mail gaat er nog uit.</summary>
    Queued,

    /// <summary>Gemaild; het tweede lid heeft zich nog niet aangemeld.</summary>
    Invited,

    /// <summary>Aanmelding van het tweede lid ingediend; wacht op het bestuur.</summary>
    Applied,

    /// <summary>Klaar: het tweede lid is een eigen lid en hangt als partner aan het hoofdlid.</summary>
    Split,
}

/// <summary>
/// Een tweepersoonslid met de stand van het splitsen. <paramref name="SecondMemberId"/> en <paramref name="SplitAt"/>
/// zijn alleen gevuld bij <see cref="SplitState.Split"/>: het tweede lid zoals het nu in de administratie staat, en
/// wanneer het bestuur die aanmelding goedkeurde.
/// </summary>
public sealed record SplitCandidate(
    Guid MemberId, string MemberNumber, string FullName, string? Email, string? SecondMemberName, SplitState State, DateTime? InvitedAt,
    int TimesInvited, Guid? ApplicationId, Guid? SecondMemberId, DateTime? SplitAt);

/// <summary><paramref name="Invited"/> is het aantal uitnodigingen dat in de wachtrij staat; <paramref name="LastAt"/> is wanneer de laatste mail ongeveer weggaat.</summary>
public sealed record SplitInviteResult(int Invited, int WithoutEmail, int Skipped, DateTime LastAt);

/// <summary>
/// Wat het splitsformulier vooraf invult (alleen met een geldige link). Niet het e-mailadres: daar hoort het eigen adres
/// van het tweede lid (besluit 2026-10-09).
/// </summary>
public sealed record SplitPrefill(string MainMemberName, string? SecondFirstName, string? SecondNamePrefix, string? SecondLastName,
    string? AddressLine, string? PostalCode, string? City);

/// <summary>
/// Tweepersoonslidmaatschappen splitsen (fase 25). Het bestuur mailt het hoofdlid een persoonlijke link naar het
/// aanmeldformulier op de website ("Ik ben al lid: lid splitsen"), met het e-mailadres en waar bekend de naam van het
/// tweede lid en het adres al ingevuld. Na de e-mailcode en goedkeuring door het bestuur wordt het tweede lid een eigen
/// lid en de partner van het hoofdlid (combinatie; het hoofdlid betaalt). De mail komt van het secretariaat (als dat een
/// afzender op het eigen domein is, anders van het systeemadres); antwoorden gaan naar het secretariaat.
/// </summary>
public sealed class MemberSplits(
    DrammersDbContext db, IOutbox outbox, IAuditLogger audit, IClock clock, ICurrentActor actor, IOptions<MailingOptions> mailingOptions)
{
    /// <summary>Berichttype in de outbox: één uitnodiging, één mail.</summary>
    public const string InviteMailMessageType = "member.split-invite-mail";

    public const string ReplyTo = "secretaris@vrolijkedrammers.nl";

    /// <summary>Afzender vóór de @; het antwoordadres bepaalt de afzender niet meer (<see cref="EmailSenderAddress"/>).</summary>
    public const string Sender = "secretaris";

    /// <summary>Hoe lang een link geldig is.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// Het contactformulier op de website, met het secretariaat al als ontvanger. Geen e-mailadressen in de mail
    /// (fase 21i); dit is de uitweg voor een tweede lid zonder eigen e-mailadres.
    /// </summary>
    public const string ContactPath = "/contact?aan=secretaris#formulier";

    /// <summary>
    /// Alle actieve tweepersoonsleden met de stand van het splitsen, ook de leden die al gesplitst zijn
    /// (<see cref="SplitState.Split"/>). Die stonden er eerst niet bij, maar het bestuur wil kunnen zien wie klaar is
    /// en wie niet, en zelf aanvinken wie een (nieuwe) uitnodiging krijgt (wens 2026-11-21).
    /// </summary>
    public async Task<IReadOnlyList<SplitCandidate>> CandidatesAsync(CancellationToken cancellationToken)
    {
        var members = await db.Members.AsNoTracking()
            .Where(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active)
            .Select(m => new { m.Id, m.MemberNumber, m.FullName, m.LastName, m.FirstName, m.Email, m.SecondMemberName, m.MembershipKind, m.EbStatusRaw })
            .ToListAsync(cancellationToken);
        var twoPersons = members.Where(m => (m.MembershipKind ?? Contributions.KindFromEBoekhouden(m.EbStatusRaw)) == MembershipKind.TwoPersons).ToList();
        var ids = twoPersons.Select(m => m.Id).ToList();
        var invitations = await db.MembershipSplitInvitations.AsNoTracking().Where(i => ids.Contains(i.MemberId))
            .ToDictionaryAsync(i => i.MemberId, cancellationToken);
        var partners = await db.Members.AsNoTracking()
            .Where(p => p.PayerMemberId != null && ids.Contains(p.PayerMemberId.Value))
            .Select(p => new { p.Id, p.FullName, PayerMemberId = p.PayerMemberId!.Value })
            .ToListAsync(cancellationToken);
        var second = partners.GroupBy(p => p.PayerMemberId).ToDictionary(g => g.Key, g => g.First());
        var applications = await db.MembershipApplications.AsNoTracking()
            .Where(a => a.SplitFromMemberId != null && ids.Contains(a.SplitFromMemberId.Value)
                && (a.Status == ApplicationStatus.Submitted || a.Status == ApplicationStatus.InReview || a.Status == ApplicationStatus.Approved
                    || a.Status == ApplicationStatus.Provisioning || a.Status == ApplicationStatus.ProvisioningFailed
                    || a.Status == ApplicationStatus.Activated))
            .Select(a => new { a.Id, a.Status, a.DecisionAt, MemberId = a.SplitFromMemberId!.Value })
            .ToListAsync(cancellationToken);
        // Een goedgekeurde aanmelding is "klaar" zodra het tweede lid eraan hangt; tot die tijd wacht hij nog op het
        // bestuur of op de worker, en blijft het hoofdlid bij de leden die nog niet gesplitst zijn staan.
        var applied = applications.Where(a => !second.ContainsKey(a.MemberId)).GroupBy(a => a.MemberId)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var splitAt = applications.GroupBy(a => a.MemberId)
            .ToDictionary(g => g.Key, g => g.Max(a => a.DecisionAt));

        return [.. twoPersons.OrderBy(m => m.LastName ?? m.FullName).ThenBy(m => m.FirstName).Select(m =>
        {
            var invitation = invitations.GetValueOrDefault(m.Id);
            var state = second.ContainsKey(m.Id) ? SplitState.Split
                : applied.ContainsKey(m.Id) ? SplitState.Applied
                : invitation is null ? SplitState.NotInvited
                : invitation.TokenHash.Length == 0 ? SplitState.Queued
                : SplitState.Invited;
            // Is er gesplitst, dan is de naam van het tweede lid zoals het nu in de administratie staat leidend; het
            // vrije veld uit e-Boekhouden was alleen een beginpunt voor het formulier.
            var partner = second.GetValueOrDefault(m.Id);
            return new SplitCandidate(m.Id, m.MemberNumber, m.FullName, m.Email, partner?.FullName ?? m.SecondMemberName, state,
                invitation?.SentAt, invitation?.TimesSent ?? 0, applied.TryGetValue(m.Id, out var appId) ? appId : null,
                partner?.Id, state == SplitState.Split ? splitAt.GetValueOrDefault(m.Id) : null);
        })];
    }

    /// <summary>
    /// Zet voor de hoofdleden een uitnodiging in de wachtrij (zonder <paramref name="memberIds"/>: iedereen die nog niet
    /// gemaild is). Elke mail bevat een nieuwe link; een eerdere link vervalt. Wie geen e-mailadres heeft of al een
    /// aanmelding heeft lopen, wordt overgeslagen.
    ///
    /// Het versturen gebeurt <b>niet</b> hier maar in de outbox, één bericht per hoofdlid, verdeeld over de tijd
    /// (<see cref="MailingOptions.MaxPerHour"/>) — net als de mailing en de advertentiefacturen. Eerder stuurde deze lus
    /// de mails zelf: op 11-11-2026 liep dat bij 134 uitnodigingen tegen de **240 seconden** van App Service aan
    /// (111 verstuurd, 3 afgeknepen, de rest bleef liggen) en vulde het bovendien de ACS-pot van 100 mails per uur,
    /// waardoor de aanmeldingen op de website vastliepen. Honderden mails horen niet binnen één HTTP-verzoek.
    /// </summary>
    public async Task<SplitInviteResult> InviteAsync(IReadOnlyCollection<Guid>? memberIds, string websiteBaseUrl, CancellationToken cancellationToken)
    {
        var candidates = await CandidatesAsync(cancellationToken);
        var targets = memberIds is null
            ? candidates.Where(c => c.State == SplitState.NotInvited).ToList()
            : candidates.Where(c => memberIds.Contains(c.MemberId) && c.State != SplitState.Split).ToList();
        if (memberIds is not null && targets.Count != memberIds.Count)
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen tweepersoonsleden zonder gekoppeld tweede lid kunnen een uitnodiging krijgen.");
        }

        var now = clock.UtcNow.UtcDateTime;
        var interval = TimeSpan.FromHours(1) / mailingOptions.Value.EffectiveMaxPerHour;
        int invited = 0, withoutEmail = 0, skipped = 0;
        var last = now;
        foreach (var target in targets)
        {
            if (target.State == SplitState.Applied)
            {
                skipped++;
                continue;
            }

            if (target.Email is not { Length: > 0 } address || !System.Net.Mail.MailAddress.TryCreate(address, out _))
            {
                withoutEmail++;
                continue;
            }

            var invitation = await db.MembershipSplitInvitations.SingleOrDefaultAsync(i => i.MemberId == target.MemberId, cancellationToken);
            if (invitation is null)
            {
                invitation = new MembershipSplitInvitation { Id = IdGenerator.NewId(), MemberId = target.MemberId, TokenHash = string.Empty, SentTo = address };
                db.MembershipSplitInvitations.Add(invitation);
            }

            // De link wordt pas in de handler gemaakt, zodat alleen de hash in de database komt (zie
            // MembershipSplitInvitation) en er nooit een leesbare link in de outbox staat. Een lege hash betekent
            // daarom "staat in de wachtrij" en laat de eerdere link meteen vervallen.
            invitation.TokenHash = string.Empty;
            invitation.SentTo = address;
            invitation.SentAt = now;
            invitation.SentBy = actor.UserId;

            last = now + (interval * invited);
            outbox.Enqueue(InviteMailMessageType, new InviteMail(target.MemberId, websiteBaseUrl), invited == 0 ? null : last);
            invited++;
        }

        await db.SaveChangesAsync(cancellationToken);
        var result = new SplitInviteResult(invited, withoutEmail, skipped, last);
        await audit.WriteAsync(new AuditEntry("member.split-invited", "Member", "split", null, JsonSerializer.Serialize(result, JsonSerializerOptions.Web)),
            cancellationToken);
        return result;
    }

    /// <summary>Eén uitnodigingsmail. Het adres van de website zit erbij omdat de worker geen HTTP-verzoek heeft om het uit te lezen.</summary>
    public sealed record InviteMail(Guid MemberId, string WebsiteBaseUrl);

    /// <summary>Een nieuwe link; alleen de <see cref="Hash(string)"/> ervan wordt bewaard.</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Het hoofdlid bij een link, of een 404 als de link onbekend of verlopen is of het lid al gesplitst is.</summary>
    public async Task<Member> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token.Trim());
        var since = clock.UtcNow.UtcDateTime - LinkLifetime;
        var invitation = await db.MembershipSplitInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == hash && i.SentAt >= since, cancellationToken);
        var member = invitation is null ? null : await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == invitation.MemberId, cancellationToken);
        if (member is null || member.EffectiveStatus != MembershipStatus.Active)
        {
            throw new DomainException(ErrorCodes.NotFound, "Deze link is niet (meer) geldig. Vraag het secretariaat om een nieuwe.", DomainErrorKind.NotFound);
        }

        if (await db.Members.AnyAsync(m => m.PayerMemberId == member.Id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.NotFound, "Dit lidmaatschap is al gesplitst.", DomainErrorKind.NotFound);
        }

        return member;
    }

    public async Task<SplitPrefill> PrefillAsync(string token, CancellationToken cancellationToken)
    {
        var member = await ResolveAsync(token, cancellationToken);
        var second = member.SecondMemberName is { Length: > 0 } name ? DutchNameParser.Parse(name) : null;
        return new SplitPrefill(member.FirstName ?? member.FullName, second?.FirstName, second?.NamePrefix,
            second?.LastName ?? member.LastName, member.AddressLine, member.PostalCode, member.City);
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// De uitnodiging aan het hoofdlid. Zegt met zoveel woorden dat het tweede lid een <b>eigen</b> e-mailadres nodig
    /// heeft (wens 2026-11-21): dat adres hoort bij zijn eigen plek in de app, en het formulier weigert het adres van
    /// het hoofdlid. Wie geen eigen adres heeft, krijgt het contactformulier als uitweg.
    /// </summary>
    public static EmailMessage InvitationMail(string to, string firstName, string? secondMemberName, string link, string contactLink)
    {
        const string Subject = "Registreer het tweede lid van je lidmaatschap";
        var who = string.IsNullOrWhiteSpace(secondMemberName) ? "het tweede lid" : secondMemberName.Trim();
        var text = $"""
            Beste {firstName},

            Je bent samen met {who} lid van CV De Vrolijke Drammers. Voortaan krijgt ieder lid een eigen lidmaatschap, met een eigen
            plek in de app. Daarvoor vragen we je om {who} te registreren. De contributie blijft gelijk: jullie betalen samen het
            combinatietarief, en dat wordt zoals altijd van jouw rekening geïncasseerd.

            Houd bij het invullen één ding bij de hand: {who} heeft een eigen e-mailadres nodig. Dat adres hoort bij de eigen plek in
            de app, dus jouw e-mailadres kan daar niet nog een keer voor gebruikt worden. Heeft {who} geen eigen e-mailadres? Laat het
            ons dan weten via het contactformulier, dan regelen we het samen:
            {contactLink}

            Registreren gaat via deze persoonlijke link (90 dagen geldig):
            {link}

            Met vriendelijke groet,
            De Vrolijke Drammers
            """;
        var html = $"""
            <p>Beste {WebUtility.HtmlEncode(firstName)},</p>
            <p>Je bent samen met {WebUtility.HtmlEncode(who)} lid van CV De Vrolijke Drammers. Voortaan krijgt ieder lid een eigen lidmaatschap, met een eigen plek in de app. Daarvoor vragen we je om {WebUtility.HtmlEncode(who)} te registreren. De contributie blijft gelijk: jullie betalen samen het combinatietarief, en dat wordt zoals altijd van jouw rekening geïncasseerd.</p>
            <p>Houd bij het invullen één ding bij de hand: {WebUtility.HtmlEncode(who)} heeft een <strong>eigen e-mailadres</strong> nodig. Dat adres hoort bij de eigen plek in de app, dus jouw e-mailadres kan daar niet nog een keer voor gebruikt worden. Heeft {WebUtility.HtmlEncode(who)} geen eigen e-mailadres? Laat het ons dan weten via <a href="{WebUtility.HtmlEncode(contactLink)}">het contactformulier</a>, dan regelen we het samen.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;background:#ED0012;color:#fff;padding:12px 20px;border-radius:999px;text-decoration:none;font-weight:600">Tweede lid registreren</a></p>
            <p>De link is 90 dagen geldig.</p>
            <p>Met vriendelijke groet,<br>De Vrolijke Drammers</p>
            """;
        // ViaMailbox: via de mailbox secretaris@ in Microsoft 365 en dus niet uit de ACS-pot van 100 mails per uur, die
        // de aanmeldingen op de website deelt. Staat secretaris@ niet in Email__MailboxSenders, dan valt hij terug op ACS.
        return new EmailMessage(to, Subject, text, html, ReplyTo, Sender, ViaMailbox: true);
    }
}

/// <summary>
/// Verstuurt één uitnodiging uit de outbox. De link wordt hier gemaakt en alleen als hash vastgelegd; een uitnodiging
/// met een lege hash staat nog in de wachtrij. Mislukt het versturen, dan blijft de hash leeg en biedt de outbox het
/// bericht later opnieuw aan — het hoofdlid krijgt dus nooit een link die niet werkt. Geeft de outbox het na vijf
/// pogingen op, dan blijft de uitnodiging op "in de wachtrij" staan en is opnieuw mailen nodig.
///
/// Omdat een gevulde hash "al verstuurd" betekent, levert twee keer klikken één mail op, en telt
/// <see cref="MembershipSplitInvitation.TimesSent"/> verstuurde mails in plaats van kliks.
/// </summary>
public sealed class MemberSplitInviteMailHandler(DrammersDbContext db, IEmailSender email, IClock clock) : IOutboxMessageHandler
{
    public string Type => MemberSplits.InviteMailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<MemberSplits.InviteMail>(message.Payload, JsonSerializerOptions.Web)!;
        var invitation = await db.MembershipSplitInvitations.SingleOrDefaultAsync(i => i.MemberId == payload.MemberId, cancellationToken);
        var member = invitation is null
            ? null
            : await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == payload.MemberId, cancellationToken);
        if (invitation is null || member is null || invitation.TokenHash.Length > 0)
        {
            // Teruggedraaid, lid weg, of al verstuurd (het bericht is opnieuw aangeboden): niets te doen.
            return;
        }

        var token = MemberSplits.NewToken();
        var website = payload.WebsiteBaseUrl.TrimEnd('/');
        await email.SendAsync(MemberSplits.InvitationMail(invitation.SentTo, member.FirstName ?? member.FullName, member.SecondMemberName,
            $"{website}/lid-worden/?splitsen={token}", website + MemberSplits.ContactPath), cancellationToken);
        invitation.TokenHash = MemberSplits.Hash(token);
        invitation.SentAt = clock.UtcNow.UtcDateTime;
        invitation.TimesSent++;
        await db.SaveChangesAsync(cancellationToken);
    }
}
