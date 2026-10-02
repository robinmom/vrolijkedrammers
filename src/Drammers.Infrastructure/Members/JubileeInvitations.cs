using System.Net;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

public sealed record JubileeInvitationTemplate(string Subject, string Body, string ReplyTo);

public sealed record JubileeInvitationResult(int Invited, int AlreadyInvited, int WithoutEmail);

/// <summary>
/// Uitnodigingen voor de huldiging van jubilarissen (fase 20b). De tekst is een sjabloon in de configuratie met
/// invulvelden; elke jubilaris krijgt per carnavalsjaar hooguit één uitnodiging, verstuurd via de outbox. Zonder eigen
/// domein in ACS komt de mail van DoNotReply; antwoorden gaan naar het Reply-To-adres (standaard het secretariaat).
/// </summary>
public sealed class JubileeInvitations(
    DrammersDbContext db, Jubilees jubilees, IOutbox outbox, IAuditLogger audit, IClock clock, ICurrentActor actor)
{
    public const string MailMessageType = "jubilee.invitation-mail";

    public const string DefaultSubject = "Uitnodiging: huldiging jubilarissen {carnavalsjaar}";

    public const string DefaultBody = """
        Beste {voornaam},

        Dit jaar ben je {jaren} jaar lid. Dit willen we niet zomaar voorbij laten gaan. We nodigen je daarom uit om op carnavalsdinsdag gehuldigd te worden.

        Met vriendelijke groet,

        Robin Mom
        """;

    public const string DefaultReplyTo = "secretaris@vrolijkedrammers.nl";

    /// <summary>De afzender (vóór de @) zodra het eigen domein in ACS is gekoppeld.</summary>
    public const string SenderLocalPart = "secretaris";

    public static readonly IReadOnlyList<string> Placeholders = ["{voornaam}", "{naam}", "{jaren}", "{carnavalsjaar}"];

    public sealed record InvitationMail(Guid InvitationId);

    public async Task<JubileeInvitationTemplate> GetTemplateAsync(CancellationToken cancellationToken)
    {
        string[] keys = [AppConfigurationKeys.JubileeInvitationSubject, AppConfigurationKeys.JubileeInvitationBody, AppConfigurationKeys.JubileeInvitationReplyTo];
        var values = await db.AppConfiguration.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        string Get(string key, string fallback) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
        return new JubileeInvitationTemplate(
            Get(AppConfigurationKeys.JubileeInvitationSubject, DefaultSubject),
            Get(AppConfigurationKeys.JubileeInvitationBody, DefaultBody),
            Get(AppConfigurationKeys.JubileeInvitationReplyTo, DefaultReplyTo));
    }

    public async Task SetTemplateAsync(JubileeInvitationTemplate template, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(template.Subject) || string.IsNullOrWhiteSpace(template.Body))
        {
            throw new DomainException(ErrorCodes.Validation, "Onderwerp en tekst zijn verplicht.");
        }

        if (!System.Net.Mail.MailAddress.TryCreate(template.ReplyTo, out _))
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een geldig e-mailadres in voor antwoorden.");
        }

        var values = new Dictionary<string, string>
        {
            [AppConfigurationKeys.JubileeInvitationSubject] = template.Subject.Trim(),
            [AppConfigurationKeys.JubileeInvitationBody] = template.Body.Trim(),
            [AppConfigurationKeys.JubileeInvitationReplyTo] = template.ReplyTo.Trim(),
        };
        var before = await GetTemplateAsync(cancellationToken);
        var settings = await db.AppConfiguration.Where(s => values.Keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, cancellationToken);
        foreach (var (key, value) in values)
        {
            if (settings.TryGetValue(key, out var setting))
            {
                setting.Value = value;
            }
            else
            {
                db.AppConfiguration.Add(new AppConfigurationSetting { Key = key, Value = value });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("config.jubilee-invitation.changed", "AppConfiguration", "jubilee_invitation",
            JsonSerializer.Serialize(before, JsonSerializerOptions.Web), JsonSerializer.Serialize(values, JsonSerializerOptions.Web)), cancellationToken);
    }

    /// <summary>
    /// Nodigt de jubilarissen van het carnavalsjaar uit; zonder <paramref name="memberIds"/> allemaal. Wie dit jaar al is
    /// uitgenodigd of geen e-mailadres heeft, wordt overgeslagen en geteld.
    /// </summary>
    public async Task<JubileeInvitationResult> InviteAsync(int? carnivalYearId, IReadOnlyCollection<Guid>? memberIds, CancellationToken cancellationToken)
    {
        var report = await jubilees.BuildAsync(carnivalYearId, cancellationToken);
        var targets = report.Jubilarians.Where(j => memberIds is null || memberIds.Contains(j.MemberId)).ToList();
        if (memberIds is not null && targets.Count != memberIds.Count)
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen jubilarissen van dit carnavalsjaar kunnen worden uitgenodigd.");
        }

        var ids = targets.Select(t => t.MemberId).ToList();
        var already = await db.JubileeInvitations.AsNoTracking()
            .Where(i => i.CarnivalYearId == report.CarnivalYearId && ids.Contains(i.MemberId)).Select(i => i.MemberId).ToListAsync(cancellationToken);
        var emails = await db.Members.AsNoTracking().Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Email }).ToDictionaryAsync(m => m.Id, m => m.Email, cancellationToken);

        var now = clock.UtcNow.UtcDateTime;
        int invited = 0, withoutEmail = 0;
        foreach (var target in targets.Where(t => !already.Contains(t.MemberId)))
        {
            if (emails.GetValueOrDefault(target.MemberId) is not { Length: > 0 } email || !System.Net.Mail.MailAddress.TryCreate(email, out _))
            {
                withoutEmail++;
                continue;
            }

            var invitation = new JubileeInvitation
            {
                Id = IdGenerator.NewId(),
                MemberId = target.MemberId,
                CarnivalYearId = report.CarnivalYearId,
                Years = target.Years,
                SentTo = email,
                InvitedAt = now,
                InvitedBy = actor.UserId,
            };
            db.JubileeInvitations.Add(invitation);
            outbox.Enqueue(MailMessageType, new InvitationMail(invitation.Id));
            invited++;
        }

        await db.SaveChangesAsync(cancellationToken);
        var result = new JubileeInvitationResult(invited, already.Count, withoutEmail);
        await audit.WriteAsync(new AuditEntry("member.jubilee-invited", "CarnivalYear", report.CarnivalYearId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            null, JsonSerializer.Serialize(result, JsonSerializerOptions.Web)), cancellationToken);
        return result;
    }

    /// <summary>Vult de invulvelden; onbekende accolades blijven staan.</summary>
    public static string Fill(string template, string firstName, string fullName, int years, string carnivalYear) =>
        template.Replace("{voornaam}", firstName, StringComparison.OrdinalIgnoreCase)
            .Replace("{naam}", fullName, StringComparison.OrdinalIgnoreCase)
            .Replace("{jaren}", years.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{carnavalsjaar}", carnivalYear, StringComparison.OrdinalIgnoreCase);

    /// <summary>Platte tekst naar eenvoudige HTML: alinea's op lege regels, regeleinden als &lt;br&gt;.</summary>
    public static string ToHtml(string text)
    {
        var html = new StringBuilder();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            html.Append("<p>").Append(string.Join("<br>", paragraph.Split('\n').Select(l => WebUtility.HtmlEncode(l.Trim())))).Append("</p>\n");
        }

        return html.ToString();
    }
}

/// <summary>Verstuurt één uitnodiging (outbox, na de commit); met de tekst van dat moment.</summary>
public sealed class JubileeInvitationMailHandler(DrammersDbContext db, JubileeInvitations invitations, IEmailSender email, IClock clock)
    : IOutboxMessageHandler
{
    public string Type => JubileeInvitations.MailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var id = JsonSerializer.Deserialize<JubileeInvitations.InvitationMail>(message.Payload, JsonSerializerOptions.Web)!.InvitationId;
        var invitation = await db.JubileeInvitations.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invitation is null || invitation.EmailSentAt is not null)
        {
            return;
        }

        var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == invitation.MemberId, cancellationToken);
        var year = await db.CarnivalYears.AsNoTracking().SingleAsync(y => y.Id == invitation.CarnivalYearId, cancellationToken);
        var template = await invitations.GetTemplateAsync(cancellationToken);
        var firstName = member.FirstName ?? member.FullName;
        var subject = JubileeInvitations.Fill(template.Subject, firstName, member.FullName, invitation.Years, year.Name);
        var body = JubileeInvitations.Fill(template.Body, firstName, member.FullName, invitation.Years, year.Name);

        await email.SendAsync(
            new EmailMessage(invitation.SentTo, subject, body, JubileeInvitations.ToHtml(body), template.ReplyTo, JubileeInvitations.SenderLocalPart),
            cancellationToken);
        invitation.EmailSentAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }
}
