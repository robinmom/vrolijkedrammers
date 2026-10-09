using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Applications;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

public enum SplitState
{
    /// <summary>Nog niet gemaild.</summary>
    NotInvited,

    /// <summary>Gemaild; het tweede lid heeft zich nog niet aangemeld.</summary>
    Invited,

    /// <summary>Aanmelding van het tweede lid ingediend; wacht op het bestuur.</summary>
    Applied,
}

public sealed record SplitCandidate(
    Guid MemberId, string MemberNumber, string FullName, string? Email, string? SecondMemberName, SplitState State, DateTime? InvitedAt,
    int TimesInvited, Guid? ApplicationId);

public sealed record SplitInviteResult(int Invited, int WithoutEmail, int Skipped);

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
/// lid en de partner van het hoofdlid (combinatie; het hoofdlid betaalt). De mail komt van het systeemadres; antwoorden
/// gaan naar het secretariaat.
/// </summary>
public sealed class MemberSplits(DrammersDbContext db, IEmailSender email, IAuditLogger audit, IClock clock, ICurrentActor actor)
{
    public const string ReplyTo = "secretaris@vrolijkedrammers.nl";

    /// <summary>Hoe lang een link geldig is.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(90);

    /// <summary>Actieve tweepersoonsleden zonder gekoppelde partner, met de stand van de uitnodiging.</summary>
    public async Task<IReadOnlyList<SplitCandidate>> CandidatesAsync(CancellationToken cancellationToken)
    {
        var members = await db.Members.AsNoTracking()
            .Where(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active)
            .Where(m => !db.Members.Any(p => p.PayerMemberId == m.Id))
            .Select(m => new { m.Id, m.MemberNumber, m.FullName, m.LastName, m.FirstName, m.Email, m.SecondMemberName, m.MembershipKind, m.EbStatusRaw })
            .ToListAsync(cancellationToken);
        var twoPersons = members.Where(m => (m.MembershipKind ?? Contributions.KindFromEBoekhouden(m.EbStatusRaw)) == MembershipKind.TwoPersons).ToList();
        var ids = twoPersons.Select(m => m.Id).ToList();
        var invitations = await db.MembershipSplitInvitations.AsNoTracking().Where(i => ids.Contains(i.MemberId))
            .ToDictionaryAsync(i => i.MemberId, cancellationToken);
        var applications = await db.MembershipApplications.AsNoTracking()
            .Where(a => a.SplitFromMemberId != null && ids.Contains(a.SplitFromMemberId.Value)
                && (a.Status == ApplicationStatus.Submitted || a.Status == ApplicationStatus.InReview || a.Status == ApplicationStatus.Approved
                    || a.Status == ApplicationStatus.Provisioning || a.Status == ApplicationStatus.ProvisioningFailed))
            .Select(a => new { a.Id, MemberId = a.SplitFromMemberId!.Value })
            .ToListAsync(cancellationToken);
        var applied = applications.GroupBy(a => a.MemberId).ToDictionary(g => g.Key, g => g.First().Id);

        return [.. twoPersons.OrderBy(m => m.LastName ?? m.FullName).ThenBy(m => m.FirstName).Select(m =>
        {
            var invitation = invitations.GetValueOrDefault(m.Id);
            var state = applied.ContainsKey(m.Id) ? SplitState.Applied : invitation is null ? SplitState.NotInvited : SplitState.Invited;
            return new SplitCandidate(m.Id, m.MemberNumber, m.FullName, m.Email, m.SecondMemberName, state, invitation?.SentAt,
                invitation?.TimesSent ?? 0, applied.TryGetValue(m.Id, out var appId) ? appId : null);
        })];
    }

    /// <summary>
    /// Mailt de hoofdleden (zonder <paramref name="memberIds"/>: iedereen die nog niet gemaild is). Elke mail bevat een
    /// nieuwe link; een eerdere link vervalt. Wie geen e-mailadres heeft of al een aanmelding heeft lopen, wordt overgeslagen.
    /// </summary>
    public async Task<SplitInviteResult> InviteAsync(IReadOnlyCollection<Guid>? memberIds, string websiteBaseUrl, CancellationToken cancellationToken)
    {
        var candidates = await CandidatesAsync(cancellationToken);
        var targets = memberIds is null
            ? candidates.Where(c => c.State == SplitState.NotInvited).ToList()
            : candidates.Where(c => memberIds.Contains(c.MemberId)).ToList();
        if (memberIds is not null && targets.Count != memberIds.Count)
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen tweepersoonsleden zonder gekoppeld tweede lid kunnen een uitnodiging krijgen.");
        }

        int invited = 0, withoutEmail = 0, skipped = 0;
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

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var invitation = await db.MembershipSplitInvitations.SingleOrDefaultAsync(i => i.MemberId == target.MemberId, cancellationToken);
            if (invitation is null)
            {
                invitation = new MembershipSplitInvitation { Id = IdGenerator.NewId(), MemberId = target.MemberId, TokenHash = Hash(token), SentTo = address };
                db.MembershipSplitInvitations.Add(invitation);
            }

            invitation.TokenHash = Hash(token);
            invitation.SentTo = address;
            invitation.SentAt = clock.UtcNow.UtcDateTime;
            invitation.SentBy = actor.UserId;
            invitation.TimesSent++;

            var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == target.MemberId, cancellationToken);
            await email.SendAsync(InvitationMail(address, member.FirstName ?? member.FullName, member.SecondMemberName,
                $"{websiteBaseUrl.TrimEnd('/')}/lid-worden/?splitsen={token}"), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            invited++;
        }

        var result = new SplitInviteResult(invited, withoutEmail, skipped);
        await audit.WriteAsync(new AuditEntry("member.split-invited", "Member", "split", null, JsonSerializer.Serialize(result, JsonSerializerOptions.Web)),
            cancellationToken);
        return result;
    }

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

    public static EmailMessage InvitationMail(string to, string firstName, string? secondMemberName, string link)
    {
        const string Subject = "Registreer het tweede lid van je lidmaatschap";
        var who = string.IsNullOrWhiteSpace(secondMemberName) ? "het tweede lid" : secondMemberName.Trim();
        var text = $"""
            Beste {firstName},

            Je bent samen met {who} lid van CV De Vrolijke Drammers. Voortaan krijgt ieder lid een eigen lidmaatschap, met een eigen
            plek in de app. Daarvoor vragen we je om {who} te registreren. De contributie blijft gelijk: jullie betalen samen het
            combinatietarief, en dat wordt zoals altijd van jouw rekening geïncasseerd.

            Registreren gaat via deze persoonlijke link (90 dagen geldig):
            {link}

            Je e-mailadres is al ingevuld. Heeft {who} een eigen e-mailadres, vul dat dan in.

            Met vriendelijke groet,
            Het bestuur van CV De Vrolijke Drammers
            """;
        var html = $"""
            <p>Beste {WebUtility.HtmlEncode(firstName)},</p>
            <p>Je bent samen met {WebUtility.HtmlEncode(who)} lid van CV De Vrolijke Drammers. Voortaan krijgt ieder lid een eigen lidmaatschap, met een eigen plek in de app. Daarvoor vragen we je om {WebUtility.HtmlEncode(who)} te registreren. De contributie blijft gelijk: jullie betalen samen het combinatietarief, en dat wordt zoals altijd van jouw rekening geïncasseerd.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;background:#ED0012;color:#fff;padding:12px 20px;border-radius:999px;text-decoration:none;font-weight:600">Tweede lid registreren</a></p>
            <p>De link is 90 dagen geldig. Je e-mailadres is al ingevuld. Heeft {WebUtility.HtmlEncode(who)} een eigen e-mailadres, vul dat dan in.</p>
            <p>Met vriendelijke groet,<br>Het bestuur van CV De Vrolijke Drammers</p>
            """;
        return new EmailMessage(to, Subject, text, html, ReplyTo);
    }
}
