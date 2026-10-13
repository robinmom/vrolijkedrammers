using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Import.Sync;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Drammers.Infrastructure.Identity;

/// <summary>
/// Welkomstmails met de inloguitleg (2026-10-12, inloggen met wachtwoord). Het bestuur stuurt ze op een zelfgekozen moment
/// in één keer naar alle huidige leden en de overige accounts (ouders, beheerders), of opnieuw naar één lid ("wachtwoord
/// vergeten" lukt niet, mail kwijt). Elk lid krijgt meteen een account (ook bij Entra); de mails gaan via de outbox met
/// <see cref="Interval"/> ertussen, binnen de limiet van de mailservice.
/// </summary>
public sealed class AccountWelcomes(
    DrammersDbContext db,
    IOutbox outbox,
    IAuditLogger audit,
    MemberAccounts accounts,
    EntraLogins logins,
    IEmailSender email,
    IClock clock,
    ILogger<AccountWelcomes> logger)
{
    public const string MessageType = "account.welcome";

    /// <summary>Ruim onder de 100 mails per uur van Azure Communication Services.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(40);

    /// <summary>Eén welkomstmail; <paramref name="Resend"/> stuurt ook als er al een is gestuurd.</summary>
    public sealed record WelcomeMessage(Guid? MemberId, Guid? UserId, bool Resend);

    /// <summary>Wat "Welkomstmail naar iedereen" zou doen.</summary>
    /// <param name="Members">Leden die een mail krijgen.</param>
    /// <param name="OtherAccounts">Accounts zonder lid (ouders, beheerders) die een mail krijgen.</param>
    /// <param name="AlreadySent">Al eerder een welkomstmail gekregen (worden overgeslagen).</param>
    /// <param name="WithoutEmail">Actieve leden zonder e-mailadres.</param>
    /// <param name="Children">Kinderen: geen eigen account, de ouder logt in.</param>
    /// <param name="SharedEmail">Volwassenen met het e-mailadres van een ander lid: geen eigen account.</param>
    /// <param name="PasswordSignIn">Inloggen met wachtwoord staat aan (anders kan versturen nog niet).</param>
    /// <param name="EstimatedMinutes">Hoe lang het versturen ongeveer duurt.</param>
    /// <param name="Recipients">Wie de mail krijgt (naam en e-mailadres; bij iedereen de eerste 500).</param>
    public sealed record Preview(
        int Members, int OtherAccounts, int AlreadySent, int WithoutEmail, int Children, int SharedEmail, bool PasswordSignIn, int EstimatedMinutes,
        IReadOnlyList<WelcomeRecipient> Recipients);

    public sealed record WelcomeRecipient(string Name, string Email);

    /// <summary>
    /// Wie een welkomstmail krijgt (2026-10-13): iedereen, of een selectie, bijvoorbeeld eerst het kader en de gesplitste
    /// leden en op 11-11 de rest. Binnen een selectie telt een lid mee als het aan een van de keuzes voldoet.
    /// </summary>
    /// <param name="Everyone">Alle huidige leden en overige accounts (de rest van de velden telt dan niet).</param>
    /// <param name="RoleCodes">Accounts met een van deze rollen.</param>
    /// <param name="GroupIds">Leden van een van deze groepen.</param>
    /// <param name="Committee">Het kader op de website (gekoppeld aan een lid).</param>
    /// <param name="Split">Gesplitste tweepersoonslidmaatschappen: het hoofdlid en het tweede lid.</param>
    /// <param name="MemberIds">Losse leden.</param>
    public sealed record WelcomeSelection(
        bool Everyone, IReadOnlyList<string>? RoleCodes = null, IReadOnlyList<Guid>? GroupIds = null, bool Committee = false, bool Split = false,
        IReadOnlyList<Guid>? MemberIds = null)
    {
        public static readonly WelcomeSelection All = new(true);
    }

    /// <summary>Keuzes voor de selectie in het portal.</summary>
    public sealed record SelectionOptions(IReadOnlyList<SelectionOption> Roles, IReadOnlyList<SelectionOption> Groups, int CommitteeMembers, int SplitMembers);

    public sealed record SelectionOption(string Key, string Name);

    private sealed record Target(Guid? MemberId, Guid? UserId, string Name, string Email);

    public async Task<SelectionOptions> OptionsAsync(CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.SortOrder).Select(r => new SelectionOption(r.Code, r.Name)).ToListAsync(cancellationToken);
        var groups = await db.Groups.AsNoTracking().Where(g => g.Active).OrderBy(g => g.Name)
            .Select(g => new SelectionOption(g.Id.ToString(), g.Name)).ToListAsync(cancellationToken);
        var committee = await db.CommitteeMembers.AsNoTracking().Where(c => c.MemberId != null).Select(c => c.MemberId).Distinct().CountAsync(cancellationToken);
        var split = (await SplitMemberIdsAsync(cancellationToken)).Count;
        return new SelectionOptions(roles, groups, committee, split);
    }

    public Task<Preview> PreviewAsync(CancellationToken cancellationToken) => PreviewAsync(WelcomeSelection.All, cancellationToken);

    public async Task<Preview> PreviewAsync(WelcomeSelection selection, CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(selection, cancellationToken);
        var count = plan.Targets.Count;
        return new Preview(
            plan.Targets.Count(t => t.MemberId is not null), plan.Targets.Count(t => t.UserId is not null), plan.AlreadySent,
            plan.WithoutEmail, plan.Children, plan.SharedEmail, logins.PasswordSignIn,
            (int)Math.Ceiling(count * Interval.TotalMinutes),
            [.. plan.Targets.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).Take(500).Select(t => new WelcomeRecipient(t.Name, t.Email))]);
    }

    public Task<int> SendToAllAsync(CancellationToken cancellationToken) => SendAsync(WelcomeSelection.All, cancellationToken);

    /// <summary>Zet de welkomstmails in de wachtrij. Een tweede keer starten stuurt niemand dubbel.</summary>
    public async Task<int> SendAsync(WelcomeSelection selection, CancellationToken cancellationToken)
    {
        if (!logins.PasswordSignIn)
        {
            throw new DomainException(
                ErrorCodes.PasswordSignInOff, "Inloggen met wachtwoord staat nog niet aan (Accounts__PasswordSignIn); zie docs/runbooks/accounts-wachtwoord.md.",
                DomainErrorKind.Conflict);
        }

        var plan = await PlanAsync(selection, cancellationToken);
        if (!selection.Everyone && plan.Targets.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "In deze selectie is niemand die nog een welkomstmail moet krijgen.");
        }

        var start = clock.UtcNow.UtcDateTime;
        for (var i = 0; i < plan.Targets.Count; i++)
        {
            var target = plan.Targets[i];
            outbox.Enqueue(MessageType, new WelcomeMessage(target.MemberId, target.UserId, false), start + (i * Interval));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(selection.Everyone ? "account.welcome-all" : "account.welcome-selection", "User", selection.Everyone ? "all" : "selection", null,
                JsonSerializer.Serialize(new { count = plan.Targets.Count, selection }, JsonSerializerOptions.Web)),
            cancellationToken);
        return plan.Targets.Count;
    }

    /// <summary>Stuurt de welkomstmail (opnieuw) naar één lid; maakt zo nodig eerst het account.</summary>
    public async Task ResendToMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        var hasAccount = await db.Users.AnyAsync(u => u.MemberId == memberId && u.AccountStatus != AccountStatus.Deleted, cancellationToken);
        if (!hasAccount)
        {
            if (member.EffectiveStatus != MembershipStatus.Active)
            {
                throw new DomainException(ErrorCodes.MemberNotEligible, "Dit lid is niet actief.", DomainErrorKind.Conflict);
            }

            if (string.IsNullOrWhiteSpace(member.Email))
            {
                throw new DomainException(ErrorCodes.MemberNotEligible, "Dit lid heeft geen e-mailadres.", DomainErrorKind.Conflict);
            }

            if (ChildAccounts.IsChild(member, Today))
            {
                throw new DomainException(
                    ErrorCodes.MemberNotEligible, "Dit lid is een kind: de ouder logt in met een eigen account.", DomainErrorKind.Conflict);
            }
        }

        outbox.Enqueue(MessageType, new WelcomeMessage(memberId, null, true));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("account.welcome-resent", "Member", memberId.ToString(), null, null), cancellationToken);
    }

    /// <summary>Stuurt de welkomstmail (opnieuw) naar een account zonder lid (ouder, beheerder) of van een lid.</summary>
    public async Task ResendToUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.AccountStatus != AccountStatus.Deleted, cancellationToken)
            ?? throw new DomainException(ErrorCodes.UserNotFound, "Gebruiker niet gevonden.", DomainErrorKind.NotFound);
        outbox.Enqueue(MessageType, user.MemberId is { } memberId ? new WelcomeMessage(memberId, null, true) : new WelcomeMessage(null, userId, true));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("account.welcome-resent", "User", userId.ToString(), null, null), cancellationToken);
    }

    /// <summary>
    /// Nieuwe leden uit de ledensync krijgen meteen een account en een welkomstmail, zodra het bestuur de welkomstmails
    /// eenmaal heeft verstuurd (vóór de lancering dus niet).
    /// </summary>
    public static async Task EnqueueForSyncedMembersAsync(
        DrammersDbContext db, IOutbox outbox, bool passwordSignIn, Guid syncJobId, CancellationToken cancellationToken)
    {
        if (!passwordSignIn || !await db.Users.AnyAsync(u => u.WelcomeSentAt != null, cancellationToken))
        {
            return;
        }

        var created = await db.SyncJobItems.AsNoTracking()
            .Where(i => i.SyncJobId == syncJobId && i.Action == SyncItemAction.Created && i.MemberId != null)
            .Select(i => i.MemberId!.Value)
            .ToListAsync(cancellationToken);
        foreach (var memberId in created)
        {
            outbox.Enqueue(MessageType, new WelcomeMessage(memberId, null, false));
        }
    }

    // ----- Worker ------------------------------------------------------------------------------------------------

    public async Task SendAsync(WelcomeMessage message, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        Guid userId;
        string to;
        string firstName;
        if (message.MemberId is { } memberId)
        {
            var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken);
            if (member is null)
            {
                return;
            }

            var user = await db.Users.AsNoTracking()
                .Where(u => u.MemberId == memberId && u.AccountStatus != AccountStatus.Deleted)
                .Select(u => new { u.Id, u.Email, u.DisplayName, u.WelcomeSentAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (user is not null)
            {
                if (user.WelcomeSentAt is not null && !message.Resend)
                {
                    return;
                }

                await logins.ResolveAsync(user.Email, user.DisplayName, cancellationToken);
                (userId, to) = (user.Id, user.Email);
            }
            else
            {
                if (member.EffectiveStatus != MembershipStatus.Active || string.IsNullOrWhiteSpace(member.Email) || ChildAccounts.IsChild(member, Today))
                {
                    return;
                }

                to = member.Email.Trim().ToLowerInvariant();
                try
                {
                    userId = await accounts.EnsureAccountAsync(to, member.FullName, member.Id, DefaultRoles.Lid, cancellationToken);
                }
                catch (DomainException ex) when (ex.Code == ErrorCodes.MemberHasAccount)
                {
                    // Het adres hoort al bij het account van een ander lid (bijv. een partner): geen tweede account.
                    logger.LogInformation("Welkomstmail voor lid {MemberId} overgeslagen: e-mailadres hoort bij een ander lid", memberId);
                    return;
                }
            }

            firstName = member.FirstName ?? member.FullName;
        }
        else if (message.UserId is { } id)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null || user.AccountStatus != AccountStatus.Active || (user.WelcomeSentAt is not null && !message.Resend))
            {
                return;
            }

            await logins.ResolveAsync(user.Email, user.DisplayName, cancellationToken);
            (userId, to, firstName) = (user.Id, user.Email, user.DisplayName.Split(' ', 2)[0]);
        }
        else
        {
            return;
        }

        await email.SendAsync(MemberAccounts.WelcomeMail(to, firstName, logins.PasswordSignIn), cancellationToken);
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.WelcomeSentAt, now), cancellationToken);
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    private sealed record Plan(List<Target> Targets, int AlreadySent, int WithoutEmail, int Children, int SharedEmail);

    /// <summary>Gesplitste lidmaatschappen: het tweede lid (Partner) en het hoofdlid dat betaalt.</summary>
    private async Task<HashSet<Guid>> SplitMemberIdsAsync(CancellationToken cancellationToken)
    {
        var partners = await db.Members.AsNoTracking().Where(m => m.MembershipKind == MembershipKind.Partner)
            .Select(m => new { m.Id, m.PayerMemberId }).ToListAsync(cancellationToken);
        return [.. partners.Select(p => p.Id), .. partners.Where(p => p.PayerMemberId != null).Select(p => p.PayerMemberId!.Value)];
    }

    private async Task<Plan> PlanAsync(WelcomeSelection selection, CancellationToken cancellationToken)
    {
        var members = (await db.Members.AsNoTracking().OrderBy(m => m.MemberNumber).ToListAsync(cancellationToken))
            .Where(m => m.EffectiveStatus == MembershipStatus.Active)
            .ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.AccountStatus != AccountStatus.Deleted)
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.MemberId, u.WelcomeSentAt, u.AccountStatus })
            .ToListAsync(cancellationToken);

        // Selectie: welke leden en accounts zonder lid meedoen (bij iedereen: alles).
        HashSet<Guid>? selectedMembers = null;
        HashSet<Guid>? selectedUsers = null;
        if (!selection.Everyone)
        {
            selectedMembers = [.. selection.MemberIds ?? []];
            selectedUsers = [];
            var today = Today;
            if (selection.RoleCodes is { Count: > 0 } codes)
            {
                var withRole = (await db.UserRoles.AsNoTracking()
                        .Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && codes.Contains(r.Code)))
                        .ToListAsync(cancellationToken))
                    .Where(ur => ur.IsValidOn(today)).Select(ur => ur.UserId).ToHashSet();
                foreach (var user in users.Where(u => withRole.Contains(u.Id)))
                {
                    if (user.MemberId is { } memberId)
                    {
                        selectedMembers.Add(memberId);
                    }
                    else
                    {
                        selectedUsers.Add(user.Id);
                    }
                }
            }

            if (selection.GroupIds is { Count: > 0 } groupIds)
            {
                var inGroups = await db.GroupMemberships.AsNoTracking().Where(gm => groupIds.Contains(gm.GroupId)).ToListAsync(cancellationToken);
                selectedMembers.UnionWith(inGroups.Where(gm => gm.IsValidOn(today)).Select(gm => gm.MemberId));
            }

            if (selection.Committee)
            {
                selectedMembers.UnionWith(await db.CommitteeMembers.AsNoTracking().Where(c => c.MemberId != null).Select(c => c.MemberId!.Value)
                    .ToListAsync(cancellationToken));
            }

            if (selection.Split)
            {
                selectedMembers.UnionWith(await SplitMemberIdsAsync(cancellationToken));
            }
        }

        var byMember = users.Where(u => u.MemberId is not null).GroupBy(u => u.MemberId!.Value).ToDictionary(g => g.Key, g => g.First());
        var activeIds = members.Select(m => m.Id).ToHashSet();

        var targets = new List<Target>();
        var claimed = users.Where(u => u.MemberId is { } m && activeIds.Contains(m)).Select(u => u.Email).ToHashSet(StringComparer.Ordinal);
        int alreadySent = 0, withoutEmail = 0, children = 0, shared = 0;
        foreach (var member in members.Where(m => selectedMembers is null || selectedMembers.Contains(m.Id)))
        {
            if (byMember.TryGetValue(member.Id, out var own))
            {
                if (own.AccountStatus != AccountStatus.Active)
                {
                    continue;
                }

                if (own.WelcomeSentAt is not null)
                {
                    alreadySent++;
                }
                else
                {
                    targets.Add(new Target(member.Id, null, member.FullName, own.Email));
                }

                continue;
            }

            if (ChildAccounts.IsChild(member, Today))
            {
                children++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(member.Email))
            {
                withoutEmail++;
                continue;
            }

            var address = member.Email.Trim().ToLowerInvariant();
            if (!claimed.Add(address))
            {
                shared++;
                continue;
            }

            targets.Add(new Target(member.Id, null, member.FullName, address));
        }

        foreach (var user in users.Where(u => u.AccountStatus == AccountStatus.Active && (u.MemberId is null || !activeIds.Contains(u.MemberId.Value))
            && (selectedUsers is null || selectedUsers.Contains(u.Id))))
        {
            if (claimed.Contains(user.Email))
            {
                continue;
            }

            if (user.WelcomeSentAt is not null)
            {
                alreadySent++;
            }
            else if (user.MemberId is null)
            {
                targets.Add(new Target(null, user.Id, user.DisplayName, user.Email));
            }
        }

        return new Plan(targets, alreadySent, withoutEmail, children, shared);
    }
}

/// <summary>Verstuurt één welkomstmail (worker).</summary>
public sealed class AccountWelcomeHandler(AccountWelcomes welcomes) : IOutboxMessageHandler
{
    public string Type => AccountWelcomes.MessageType;

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken) =>
        welcomes.SendAsync(JsonSerializer.Deserialize<AccountWelcomes.WelcomeMessage>(message.Payload, JsonSerializerOptions.Web)!, cancellationToken);
}
