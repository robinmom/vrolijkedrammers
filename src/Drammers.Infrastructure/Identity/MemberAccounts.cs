using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.AccountRequests;
using Drammers.Modules.Identity.Provisioning;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Identity;

/// <summary>
/// Accounts voor leden (ADR-014, fase 9): "Ik ben al lid"-verzoeken, beoordeling door het bestuur en de
/// provisioning-saga (lokale gebruiker met rol Lid → welkomstmail). De inlog zelf maakt het lid bij de eerste aanmelding
/// met e-mail + code; <see cref="AccountLinker"/> koppelt die aan dit account. Alleen een exacte match met
/// e-Boekhouden of een goedkeuring leidt tot een account; de aanvrager ziet nooit wat er wel of niet klopte.
/// </summary>
public sealed class MemberAccounts(
    DrammersDbContext db,
    IAuditLogger audit,
    IOutbox outbox,
    IEntraUserDirectory entra,
    IEmailSender email,
    IUserAccessService userAccess,
    ICurrentActor actor,
    IClock clock,
    AccountAdministration administration)
{
    public const string ProvisionMessageType = "account.provision";

    public const string ReminderMessageType = "account.reminder";

    /// <summary>Openstaande accountverzoeken opnieuw beoordelen (na een sync of een gewijzigd lid).</summary>
    public const string RecheckMessageType = "account-requests.recheck";

    /// <summary>
    /// Een tweede identiek verzoek dat nog op het bestuur wacht, maakt binnen deze tijd geen nieuw verzoek (herhaald
    /// tikken, bots). Afgehandelde verzoeken tellen niet: na het verwijderen van een account moet opnieuw aanvragen kunnen.
    /// </summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record ProvisionMessage(ProvisioningSourceType SourceType, string SourceId);

    public sealed record ReminderMessage(Guid MemberId);

    /// <summary>Uitkomst van opnieuw controleren: nu goedgekeurd (account wordt gemaakt), al een account, nog open.</summary>
    public sealed record RecheckResult(int Approved, int AlreadyHasAccount, int StillPending);

    // ----- Accountverzoek ("Ik ben al lid") ------------------------------------------------------------------------

    /// <summary>
    /// Legt het verzoek vast. Het zware werk (Graph, mail) gebeurt in de worker, zodat het antwoord en de responstijd
    /// bij een match en een mismatch gelijk zijn (geen enumeratie van leden of e-mailadressen).
    /// </summary>
    public async Task SubmitAccountRequestAsync(string? memberNumber, string emailAddress, string? ipAddress, CancellationToken cancellationToken)
    {
        var number = string.IsNullOrWhiteSpace(memberNumber) ? null : memberNumber.Trim();
        var normalizedEmail = emailAddress.Trim().ToLowerInvariant();
        var now = clock.UtcNow.UtcDateTime;

        var since = now - DuplicateWindow;
        if (await db.AccountRequests.AnyAsync(
                r => r.MemberNumber == number && r.Email == normalizedEmail && r.Status == AccountRequestStatus.Pending && r.RequestedAt >= since,
                cancellationToken))
        {
            return;
        }

        var request = new AccountRequest
        {
            Id = IdGenerator.NewId(),
            MemberNumber = number,
            Email = normalizedEmail,
            IpHash = ipAddress is null ? null : Hash(ipAddress),
            RequestedAt = now,
        };

        var (member, status, reason) = await EvaluateAsync(number, normalizedEmail, cancellationToken);
        request.Status = status;
        request.MismatchReason = reason;
        request.MemberId = member?.Id;
        if (status == AccountRequestStatus.Approved)
        {
            request.DecidedAt = now;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.AccountRequests.Add(request);
        if (status == AccountRequestStatus.Approved)
        {
            outbox.Enqueue(ProvisionMessageType, new ProvisionMessage(ProvisioningSourceType.AccountRequest, request.Id.ToString()));
        }
        else if (status == AccountRequestStatus.Duplicate)
        {
            // Het lid heeft al een account: een herinnering naar het adres uit e-Boekhouden (dus alleen naar de eigenaar).
            outbox.Enqueue(ReminderMessageType, new ReminderMessage(member!.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("account-request.submitted", "AccountRequest", request.Id.ToString(), null,
                JsonSerializer.Serialize(new { status = status.ToString(), reason, memberId = member?.Id }, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Beoordeelt een verzoek tegen alle leden in onze database: uit e-Boekhouden én lokaal aangemaakte leden (bijv. via
    /// "lid worden"). Een exacte, actieve match wordt direct goedgekeurd.
    /// </summary>
    private async Task<(Member? Member, AccountRequestStatus Status, string? Reason)> EvaluateAsync(
        string? number, string email, CancellationToken cancellationToken)
    {
        var (member, emailOnlyReason) = number is null
            ? await MatchByEmailAsync(email, cancellationToken)
            : (await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.MemberNumber == number, cancellationToken), null);
        var (status, reason) = member switch
        {
            null when emailOnlyReason is not null => (AccountRequestStatus.Pending, emailOnlyReason),
            null => (AccountRequestStatus.Pending, "unknown-member-number"),
            _ when !string.Equals(member.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase) => (AccountRequestStatus.Pending, "email-mismatch"),
            _ when member.EffectiveStatus != MembershipStatus.Active => (AccountRequestStatus.Pending, "member-not-active"),
            _ when IsYoungerThanOwnAccountAge(member.BirthDate) => (AccountRequestStatus.Pending, "minor"),
            _ when await HasAccountAsync(member.Id, cancellationToken) => (AccountRequestStatus.Duplicate, "has-account"),
            _ => (AccountRequestStatus.Approved, (string?)null),
        };
        return (member, status, reason);
    }

    /// <summary>
    /// Openstaande verzoeken opnieuw beoordelen, bijvoorbeeld nadat het lid later is aangemaakt, actief is gezet of een
    /// ander e-mailadres heeft gekregen. Eén verzoek (portal) of alle openstaande (portal, na een sync of ledenwijziging).
    /// Een exacte match wordt alsnog goedgekeurd en het account aangemaakt; heeft het lid inmiddels een account, dan is
    /// het verzoek afgehandeld. Er gaat geen mail naar de aanvrager behalve de gewone welkomstmail.
    /// </summary>
    public async Task<RecheckResult> RecheckAsync(Guid? requestId, CancellationToken cancellationToken)
    {
        if (requestId is { } id && (await FindRequestAsync(id, cancellationToken)).Status != AccountRequestStatus.Pending)
        {
            throw new DomainException(ErrorCodes.AccountRequestDecided, "Dit verzoek is al afgehandeld.", DomainErrorKind.Conflict);
        }

        var pending = await db.AccountRequests
            .Where(r => r.Status == AccountRequestStatus.Pending && (requestId == null || r.Id == requestId))
            .OrderBy(r => r.RequestedAt).ToListAsync(cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        var approvedMembers = new HashSet<Guid>();
        var decided = new List<(AccountRequest Request, string Outcome)>();
        var stillPending = 0;
        foreach (var request in pending)
        {
            var (member, status, reason) = await EvaluateAsync(request.MemberNumber, request.Email, cancellationToken);
            // Twee openstaande verzoeken voor hetzelfde lid: alleen het oudste maakt het account.
            if (status == AccountRequestStatus.Approved && !approvedMembers.Add(member!.Id))
            {
                (status, reason) = (AccountRequestStatus.Duplicate, "has-account");
            }

            request.MemberId = member?.Id;
            request.MismatchReason = reason;
            if (status == AccountRequestStatus.Pending)
            {
                stillPending++;
                continue;
            }

            request.Status = status;
            request.DecidedAt = now;
            request.DecidedBy = actor.UserId;
            if (status == AccountRequestStatus.Approved)
            {
                outbox.Enqueue(ProvisionMessageType, new ProvisionMessage(ProvisioningSourceType.AccountRequest, request.Id.ToString()));
            }

            decided.Add((request, status.ToString()));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var (request, outcome) in decided)
        {
            await audit.WriteAsync(new AuditEntry("account-request.rechecked", "AccountRequest", request.Id.ToString(), null,
                JsonSerializer.Serialize(new { status = outcome, memberId = request.MemberId }, Json)), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var approved = decided.Count(d => d.Outcome == nameof(AccountRequestStatus.Approved));
        return new RecheckResult(approved, decided.Count - approved, stillPending);
    }

    /// <summary>Zet een hercontrole van de openstaande verzoeken in de outbox (alleen als die er zijn); opslaan doet de aanroeper.</summary>
    public static async Task EnqueueRecheckAsync(DrammersDbContext db, IOutbox outbox, CancellationToken cancellationToken)
    {
        if (await db.AccountRequests.AnyAsync(r => r.Status == AccountRequestStatus.Pending, cancellationToken))
        {
            outbox.Enqueue(RecheckMessageType, new { });
        }
    }

    /// <summary>
    /// Alleen een e-mailadres (fase 24): het lid met dat adres. Delen meerdere leden het adres (bijvoorbeeld een gezin), dan
    /// telt alleen wie een eigen account kan krijgen (actief, oud genoeg, nog geen account); blijft er precies één over,
    /// dan is dat het lid. Anders beslist het bestuur (<c>multiple-members</c>) of volgt de gewone afhandeling.
    /// </summary>
    private async Task<(Member? Member, string? Reason)> MatchByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var members = await db.Members.AsNoTracking().Where(m => m.Email == email).ToListAsync(cancellationToken);
        if (members.Count == 0)
        {
            return (null, "unknown-email");
        }

        if (members.Count == 1)
        {
            return (members[0], null);
        }

        var candidates = new List<Member>();
        foreach (var m in members.Where(m => m.EffectiveStatus == MembershipStatus.Active && !IsYoungerThanOwnAccountAge(m.BirthDate)))
        {
            if (!await HasAccountAsync(m.Id, cancellationToken))
            {
                candidates.Add(m);
            }
        }

        return candidates.Count switch
        {
            1 => (candidates[0], null),
            0 => (members.FirstOrDefault(m => m.EffectiveStatus == MembershipStatus.Active && !IsYoungerThanOwnAccountAge(m.BirthDate)) ?? members[0], null),
            _ => (null, "multiple-members"),
        };
    }

    /// <summary>
    /// Het bestuur koppelt het verzoek aan een lid, meestal nadat het e-mailadres in e-Boekhouden is gecorrigeerd en
    /// gesynchroniseerd. Het account krijgt het e-mailadres uit e-Boekhouden, niet het ingevulde.
    /// </summary>
    public async Task ApproveAccountRequestAsync(Guid requestId, Guid memberId, CancellationToken cancellationToken)
    {
        var request = await FindRequestAsync(requestId, cancellationToken);
        if (request.Status != AccountRequestStatus.Pending)
        {
            throw new DomainException(ErrorCodes.AccountRequestDecided, "Dit verzoek is al afgehandeld.", DomainErrorKind.Conflict);
        }

        await EnsureEligibleAsync(memberId, cancellationToken);
        request.Status = AccountRequestStatus.Approved;
        request.MemberId = memberId;
        request.DecidedAt = clock.UtcNow.UtcDateTime;
        request.DecidedBy = actor.UserId;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        outbox.Enqueue(ProvisionMessageType, new ProvisionMessage(ProvisioningSourceType.AccountRequest, request.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("account-request.approved", "AccountRequest", request.Id.ToString(), null, JsonSerializer.Serialize(new { memberId }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RejectAccountRequestAsync(Guid requestId, string? reason, CancellationToken cancellationToken)
    {
        var request = await FindRequestAsync(requestId, cancellationToken);
        if (request.Status != AccountRequestStatus.Pending)
        {
            throw new DomainException(ErrorCodes.AccountRequestDecided, "Dit verzoek is al afgehandeld.", DomainErrorKind.Conflict);
        }

        request.Status = AccountRequestStatus.Rejected;
        request.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        request.DecidedAt = clock.UtcNow.UtcDateTime;
        request.DecidedBy = actor.UserId;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("account-request.rejected", "AccountRequest", request.Id.ToString(), null, JsonSerializer.Serialize(new { reason = request.RejectionReason }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ----- Account voor een lid (portal) --------------------------------------------------------------------------

    /// <summary>Het bestuur maakt direct een account voor een lid (<c>provision-account</c>); idempotent per lid.</summary>
    public async Task<Guid> ProvisionForMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        await EnsureEligibleAsync(memberId, cancellationToken);
        var sourceId = $"member:{memberId}";
        var saga = await GetOrCreateSagaAsync(ProvisioningSourceType.Manual, sourceId, memberId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        outbox.Enqueue(ProvisionMessageType, new ProvisionMessage(ProvisioningSourceType.Manual, sourceId));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.account-requested", "Member", memberId.ToString(), null, null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return saga.Id;
    }

    /// <summary>
    /// "Eigen account geven" (fase 17): een lid vanaf 15 dat onder zijn ouders stond, krijgt een account op het eigen
    /// e-mailadres (niet het adres uit e-Boekhouden, dat vaak van een ouder is). Idempotent per lid; een eerdere, nog niet
    /// voltooide poging krijgt het nieuwe adres.
    /// </summary>
    public async Task<Guid> ProvisionOwnAccountAsync(Guid memberId, string loginEmail, CancellationToken cancellationToken)
    {
        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        if (member.EffectiveStatus != MembershipStatus.Active)
        {
            throw new DomainException(ErrorCodes.MemberNotEligible, "Alleen een actief lid kan een account krijgen.", DomainErrorKind.Conflict);
        }

        if (await HasAccountAsync(memberId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.MemberHasAccount, "Dit lid heeft al een app-account.", DomainErrorKind.Conflict);
        }

        var sourceId = Members.Guardians.OwnAccountSource(memberId);
        var saga = await GetOrCreateSagaAsync(ProvisioningSourceType.Manual, sourceId, memberId, cancellationToken);
        if (saga.UserId is null)
        {
            saga.LoginEmail = loginEmail;
            saga.EntraObjectId = null;
            saga.Step = ProvisioningStep.Pending;
            saga.CompletedAt = null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        outbox.Enqueue(ProvisionMessageType, new ProvisionMessage(ProvisioningSourceType.Manual, sourceId));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.own-account-requested", "Member", memberId.ToString(), null, null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return saga.Id;
    }

    /// <summary>
    /// Het bestuur zet het app-account van een lid terug naar "geen account": inlog, rollen en apparaten vervallen; het
    /// lid in e-Boekhouden blijft. Later kan opnieuw een account worden aangemaakt.
    /// </summary>
    public async Task RemoveAccountForMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.MemberId == memberId && u.AccountStatus != AccountStatus.Deleted)
            .Select(u => new { u.Id, u.ExternalObjectId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.UserNotFound, "Dit lid heeft geen app-account.", DomainErrorKind.NotFound);

        var objectId = await administration.DeleteUserAsync(user.Id, cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.account-removed", "Member", memberId.ToString(), null, null), cancellationToken);
        if (!PendingObjectId.IsPending(objectId))
        {
            await entra.DeleteAsync(objectId, cancellationToken);
        }
    }

    /// <summary>Opnieuw proberen van een vastgelopen provisioning (portal).</summary>
    public async Task RetryAsync(Guid provisioningId, CancellationToken cancellationToken)
    {
        var saga = await db.AccountProvisioning.SingleOrDefaultAsync(p => p.Id == provisioningId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ProvisioningNotFound, "Provisioning niet gevonden.", DomainErrorKind.NotFound);
        if (saga.Step == ProvisioningStep.Completed)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (saga.SourceType == ProvisioningSourceType.MembershipApplication)
        {
            // Aanmeldingen hebben een eigen saga (lid in e-Boekhouden eerst).
            outbox.Enqueue(Members.MembershipApplications.ProvisionMessageType, new Members.MembershipApplications.ProvisionMessage(Guid.Parse(saga.SourceId)));
            await db.MembershipApplications.Where(a => a.Id == Guid.Parse(saga.SourceId) && a.Status == Modules.Membership.Applications.ApplicationStatus.ProvisioningFailed)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, Modules.Membership.Applications.ApplicationStatus.Provisioning), cancellationToken);
        }
        else
        {
            outbox.Enqueue(ProvisionMessageType, new ProvisionMessage(saga.SourceType, saga.SourceId));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("provisioning.retried", "AccountProvisioning", saga.Id.ToString(), null, null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ----- Saga (worker) ------------------------------------------------------------------------------------------

    /// <summary>
    /// Voert de saga uit of hervat hem: elke stap wordt vastgelegd, zodat een herhaling geen tweede Entra-account,
    /// gebruiker of welkomstmail oplevert (ADR-014). Een fout wordt vastgelegd en opnieuw gegooid (outbox-retry).
    /// </summary>
    public async Task RunProvisioningAsync(ProvisionMessage message, CancellationToken cancellationToken)
    {
        var memberId = await ResolveMemberIdAsync(message, cancellationToken);
        var saga = await GetOrCreateSagaAsync(message.SourceType, message.SourceId, memberId, cancellationToken);
        if (saga.Step == ProvisioningStep.Completed)
        {
            return;
        }

        saga.Attempts++;
        try
        {
            var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
                ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
            var loginEmail = saga.LoginEmail ?? member.Email?.Trim().ToLowerInvariant()
                ?? throw new DomainException(ErrorCodes.MemberNotEligible, "Het lid heeft geen e-mailadres in e-Boekhouden.", DomainErrorKind.Conflict);

            if (saga.EntraObjectId is null)
            {
                // Bestaat er al een inlog met dit e-mailadres (bijv. een beheerder die ook lid is), dan die; anders maakt
                // het lid zelf een inlog met e-mail + code en koppelt de API die bij de eerste aanmelding (AccountLinker).
                saga.EntraObjectId = await entra.FindByEmailAsync(loginEmail, cancellationToken) ?? PendingObjectId.New();
                saga.Step = ProvisioningStep.AccountCreated;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (saga.UserId is null)
            {
                saga.UserId = await CreateOrLinkUserAsync(saga.EntraObjectId, loginEmail, member.FullName, member.Id, DefaultRoles.Lid, cancellationToken);
                saga.Step = ProvisioningStep.MemberCreated;
                await db.SaveChangesAsync(cancellationToken);
                if (message.SourceId.StartsWith("own:", StringComparison.Ordinal))
                {
                    // Eigen account (fase 17): de QR staat niet meer op de telefoon van de ouder; opnieuw koppelen op de
                    // eigen telefoon telt niet mee voor het maximum aantal keer overzetten.
                    await db.Tickets.Where(t => t.MemberId == member.Id && t.BoundDeviceId != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.BoundDeviceId, (Guid?)null), cancellationToken);
                }
            }

            if (saga.Step is not (ProvisioningStep.WelcomeSent or ProvisioningStep.Completed))
            {
                await email.SendAsync(WelcomeMail(loginEmail, member.FirstName ?? member.FullName), cancellationToken);
                saga.Step = ProvisioningStep.WelcomeSent;
                await db.SaveChangesAsync(cancellationToken);
            }

            saga.Step = ProvisioningStep.Completed;
            saga.CompletedAt = clock.UtcNow.UtcDateTime;
            saga.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(
                new AuditEntry("user.provisioned", "User", saga.UserId.Value.ToString(), null,
                    JsonSerializer.Serialize(new { source = message.SourceType.ToString(), memberId }, Json)),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            var error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            await db.AccountProvisioning.Where(p => p.Id == saga.Id).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.LastError, error).SetProperty(p => p.Attempts, saga.Attempts), cancellationToken);
            throw;
        }
    }

    /// <summary>Herinneringsmail voor een lid dat al een account heeft (worker); stil als er intussen geen account meer is.</summary>
    public async Task SendReminderAsync(ReminderMessage message, CancellationToken cancellationToken)
    {
        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == message.MemberId, cancellationToken);
        if (member?.Email is not { } address || !await HasAccountAsync(member.Id, cancellationToken))
        {
            return;
        }

        await email.SendAsync(ExistingAccountMail(address.Trim().ToLowerInvariant(), member.FirstName ?? member.FullName), cancellationToken);
    }

    /// <summary>
    /// Zorgt voor een account met deze rol voor dit e-mailadres (fase 9b: nieuw lid of ouder/verzorger). Bestaat er al een
    /// account met dit adres, dan krijgt dat de rol erbij; anders een account dat wacht op de eerste aanmelding.
    /// </summary>
    public async Task<Guid> EnsureAccountAsync(string loginEmail, string displayName, Guid? memberId, string roleCode, CancellationToken cancellationToken)
    {
        var email = loginEmail.Trim().ToLowerInvariant();
        var existing = await db.Users.Where(u => u.Email == email && u.AccountStatus != AccountStatus.Deleted)
            .Select(u => u.ExternalObjectId).FirstOrDefaultAsync(cancellationToken);
        var objectId = existing ?? await entra.FindByEmailAsync(email, cancellationToken) ?? PendingObjectId.New();
        return await CreateOrLinkUserAsync(objectId, email, displayName, memberId, roleCode, cancellationToken);
    }

    private async Task<Guid> CreateOrLinkUserAsync(
        string objectId, string loginEmail, string displayName, Guid? memberId, string roleCode, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var roleId = await db.Roles.Where(r => r.Code == roleCode).Select(r => r.Id).SingleAsync(cancellationToken);
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.ExternalObjectId == objectId, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = IdGenerator.NewId(),
                ExternalObjectId = objectId,
                Email = loginEmail,
                DisplayName = displayName,
                AccountStatus = AccountStatus.Active,
            };
            db.Users.Add(user);
        }

        // Een bestaand account (bijv. een beheerder die ook lid is) wordt gekoppeld en krijgt de rol erbij. Eén account
        // hoort bij hooguit één lid: een adres dat al bij een ander lid hoort, wordt niet stil overgenomen. Uitzondering
        // (fase 17, één inlog per e-mailadres): hoort het account bij een kind onder de 15 met hetzelfde adres, dan gaat het
        // naar de volwassene en komt het kind onder "Mijn kinderen" (ouderkoppeling).
        if (memberId is { } id)
        {
            if (user.MemberId is { } other && other != id)
            {
                if (!await IsChildWithoutOwnAccountAgeAsync(other, cancellationToken))
                {
                    throw new DomainException(ErrorCodes.MemberHasAccount, "Dit e-mailadres hoort al bij het app-account van een ander lid.", DomainErrorKind.Conflict);
                }

                await MoveChildAccountToParentAsync(user, other, cancellationToken);
            }

            user.MemberId = id;
        }

        if (user.Roles.All(r => r.RoleId != roleId))
        {
            user.Roles.Add(new UserRole { UserId = user.Id, RoleId = roleId, AssignedAt = clock.UtcNow.UtcDateTime });
        }

        user.PermissionsVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        userAccess.Invalidate(objectId);
        return user.Id;
    }

    private async Task<bool> IsChildWithoutOwnAccountAgeAsync(Guid memberId, CancellationToken cancellationToken) =>
        IsYoungerThanOwnAccountAge(await db.Members.Where(m => m.Id == memberId).Select(m => m.BirthDate).SingleOrDefaultAsync(cancellationToken));

    /// <summary>Het account van een kind wordt dat van de ouder: het kind blijft gekoppeld als kind van dit account.</summary>
    private async Task MoveChildAccountToParentAsync(User user, Guid childMemberId, CancellationToken cancellationToken)
    {
        var guardians = await db.GuardianRelations.Where(g => g.MemberId == childMemberId).Select(g => g.GuardianUserId).ToListAsync(cancellationToken);
        if (!guardians.Contains(user.Id) && guardians.Count < Modules.Membership.Guardians.GuardianRelation.MaxPerChild)
        {
            var now = clock.UtcNow.UtcDateTime;
            db.GuardianRelations.Add(new Modules.Membership.Guardians.GuardianRelation
            {
                Id = IdGenerator.NewId(),
                MemberId = childMemberId,
                GuardianUserId = user.Id,
                GuardianName = user.DisplayName.Length > 100 ? user.DisplayName[..100] : user.DisplayName,
                Relationship = Modules.Membership.Guardians.GuardianRelationship.Parent,
                VerifiedAt = now,
                CreatedAt = now,
            });
        }

        var ouder = await db.Roles.Where(r => r.Code == DefaultRoles.Ouder).Select(r => r.Id).SingleAsync(cancellationToken);
        if (user.Roles.All(r => r.RoleId != ouder))
        {
            user.Roles.Add(new UserRole { UserId = user.Id, RoleId = ouder, AssignedAt = clock.UtcNow.UtcDateTime });
        }

        await audit.WriteAsync(new AuditEntry("member.account-moved-to-parent", "User", user.Id.ToString(), childMemberId.ToString(), null), cancellationToken);
    }

    private async Task<Guid> ResolveMemberIdAsync(ProvisionMessage message, CancellationToken cancellationToken)
    {
        switch (message.SourceType)
        {
            case ProvisioningSourceType.AccountRequest:
                var requestId = Guid.Parse(message.SourceId);
                return await db.AccountRequests.Where(r => r.Id == requestId && r.Status == AccountRequestStatus.Approved)
                    .Select(r => r.MemberId).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new DomainException(ErrorCodes.AccountRequestNotFound, "Goedgekeurd verzoek niet gevonden.", DomainErrorKind.NotFound);
            case ProvisioningSourceType.Manual when message.SourceId.StartsWith("member:", StringComparison.Ordinal):
                return Guid.Parse(message.SourceId["member:".Length..]);
            case ProvisioningSourceType.Manual when message.SourceId.StartsWith("own:", StringComparison.Ordinal):
                return Guid.Parse(message.SourceId["own:".Length..]);
            default:
                throw new InvalidOperationException($"Onbekende provisioningbron {message.SourceType}/{message.SourceId}");
        }
    }

    private async Task<AccountProvisioning> GetOrCreateSagaAsync(
        ProvisioningSourceType sourceType, string sourceId, Guid memberId, CancellationToken cancellationToken)
    {
        var saga = await db.AccountProvisioning.SingleOrDefaultAsync(p => p.SourceType == sourceType && p.SourceId == sourceId, cancellationToken);
        if (saga is not null)
        {
            return saga;
        }

        var memberNumber = await db.Members.Where(m => m.Id == memberId).Select(m => m.MemberNumber).SingleOrDefaultAsync(cancellationToken);
        saga = new AccountProvisioning
        {
            Id = IdGenerator.NewId(),
            SourceType = sourceType,
            SourceId = sourceId,
            Kind = ProvisioningKind.Member,
            Step = ProvisioningStep.Pending,
            MemberId = memberId,
            MemberNumber = memberNumber,
            CreatedAt = clock.UtcNow.UtcDateTime,
        };
        db.AccountProvisioning.Add(saga);
        await db.SaveChangesAsync(cancellationToken);
        return saga;
    }

    private async Task EnsureEligibleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        if (member.EffectiveStatus != MembershipStatus.Active)
        {
            throw new DomainException(ErrorCodes.MemberNotEligible, "Alleen een actief lid kan een account krijgen.", DomainErrorKind.Conflict);
        }

        if (string.IsNullOrWhiteSpace(member.Email))
        {
            throw new DomainException(ErrorCodes.MemberNotEligible, "Het lid heeft geen e-mailadres. Vul dat eerst in bij het lid (of in e-Boekhouden en synchroniseer).", DomainErrorKind.Conflict);
        }

        if (await HasAccountAsync(memberId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.MemberHasAccount, "Dit lid heeft al een app-account.", DomainErrorKind.Conflict);
        }

        if (IsYoungerThanOwnAccountAge(member.BirthDate))
        {
            throw new DomainException(ErrorCodes.MemberNotEligible,
                $"Leden jonger dan {Modules.Membership.Applications.MembershipApplication.MinimumAgeOwnAccount} krijgen geen eigen account: koppel een ouder of verzorger.",
                DomainErrorKind.Conflict);
        }
    }

    private bool IsYoungerThanOwnAccountAge(DateOnly? birthDate) =>
        Members.Guardians.AgeOn(birthDate, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)) is { } age
        && age < Modules.Membership.Applications.MembershipApplication.MinimumAgeOwnAccount;

    private Task<bool> HasAccountAsync(Guid memberId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.MemberId == memberId && u.AccountStatus != AccountStatus.Deleted, cancellationToken);

    private async Task<AccountRequest> FindRequestAsync(Guid id, CancellationToken cancellationToken) =>
        await db.AccountRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
        ?? throw new DomainException(ErrorCodes.AccountRequestNotFound, "Accountverzoek niet gevonden.", DomainErrorKind.NotFound);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Herinnering bij een nieuw verzoek terwijl er al een account is. Concepttekst (bestuur keurt goed).</summary>
    public static EmailMessage ExistingAccountMail(string to, string firstName)
    {
        const string Subject = "Je hebt al een account voor de app van De Vrolijke Drammers";
        var text = $"""
            Beste {firstName},

            Je vroeg een account aan voor de app van De Vrolijke Drammers, maar je hebt er al een.

            Zo log je in:
            1. Open de app en kies Meer → Inloggen.
            2. Vul dit e-mailadres in: {to}. Je krijgt een code per e-mail.
            3. Nog nooit ingelogd? Kies dan op de inlogpagina voor een nieuw account ("Maak er een") met dit e-mailadres.

            Lukt het niet? Neem dan contact op met het secretariaat.

            Groeten,
            De Vrolijke Drammers
            """;
        var encodedName = System.Net.WebUtility.HtmlEncode(firstName);
        var encodedTo = System.Net.WebUtility.HtmlEncode(to);
        var html = $"""
            <p>Beste {encodedName},</p>
            <p>Je vroeg een account aan voor de app van De Vrolijke Drammers, maar je hebt er al een.</p>
            <p><strong>Zo log je in:</strong></p>
            <ol><li>Open de app en kies <em>Meer → Inloggen</em>.</li><li>Vul dit e-mailadres in: {encodedTo}. Je krijgt een code per e-mail.</li><li>Nog nooit ingelogd? Kies dan op de inlogpagina voor een nieuw account (&quot;Maak er een&quot;) met dit e-mailadres.</li></ol>
            <p>Lukt het niet? Neem dan contact op met het secretariaat.</p>
            <p>Groeten,<br>De Vrolijke Drammers</p>
            """;
        return new EmailMessage(to, Subject, text, html);
    }

    /// <summary>Concepttekst; de definitieve tekst keurt het bestuur goed (aanvullende DoD fase 9).</summary>
    public static EmailMessage WelcomeMail(string to, string firstName)
    {
        const string Subject = "Je account voor de app van De Vrolijke Drammers";
        var text = $"""
            Beste {firstName},

            Je account voor de app van De Vrolijke Drammers staat klaar.

            Zo log je de eerste keer in:
            1. Open de app en kies Meer → Inloggen.
            2. Kies op de inlogpagina voor een nieuw account ("Maak er een") en vul dit e-mailadres in: {to}.
            3. Je krijgt een code per e-mail. Vul die in; een wachtwoord is niet nodig.

            Daarna log je steeds in met dit e-mailadres en een nieuwe code.

            Heb je dit niet aangevraagd? Neem dan contact op met het secretariaat.

            Groeten,
            De Vrolijke Drammers
            """;
        var encodedName = System.Net.WebUtility.HtmlEncode(firstName);
        var encodedTo = System.Net.WebUtility.HtmlEncode(to);
        var html = $"""
            <p>Beste {encodedName},</p>
            <p>Je account voor de app van De Vrolijke Drammers staat klaar.</p>
            <p><strong>Zo log je de eerste keer in:</strong></p>
            <ol><li>Open de app en kies <em>Meer → Inloggen</em>.</li><li>Kies op de inlogpagina voor een nieuw account (&quot;Maak er een&quot;) en vul dit e-mailadres in: {encodedTo}.</li><li>Je krijgt een code per e-mail. Vul die in; een wachtwoord is niet nodig.</li></ol>
            <p>Daarna log je steeds in met dit e-mailadres en een nieuwe code.</p>
            <p>Heb je dit niet aangevraagd? Neem dan contact op met het secretariaat.</p>
            <p>Groeten,<br>De Vrolijke Drammers</p>
            """;
        return new EmailMessage(to, Subject, text, html);
    }
}

/// <summary>Verstuurt de herinnering "je hebt al een account" (worker).</summary>
public sealed class MemberAccountReminderHandler(MemberAccounts accounts) : IOutboxMessageHandler
{
    public string Type => MemberAccounts.ReminderMessageType;

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken) =>
        accounts.SendReminderAsync(JsonSerializer.Deserialize<MemberAccounts.ReminderMessage>(message.Payload, JsonSerializerOptions.Web)!, cancellationToken);
}

/// <summary>Beoordeelt de openstaande accountverzoeken opnieuw (worker, na een sync of een gewijzigd lid).</summary>
public sealed class AccountRequestRecheckHandler(MemberAccounts accounts) : IOutboxMessageHandler
{
    public string Type => MemberAccounts.RecheckMessageType;

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken) => accounts.RecheckAsync(null, cancellationToken);
}

/// <summary>Voert de provisioning uit die via de outbox is aangevraagd (worker).</summary>
public sealed class MemberAccountProvisioningHandler(MemberAccounts accounts) : IOutboxMessageHandler
{
    public string Type => MemberAccounts.ProvisionMessageType;

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken) =>
        accounts.RunProvisioningAsync(JsonSerializer.Deserialize<MemberAccounts.ProvisionMessage>(message.Payload, JsonSerializerOptions.Web)!, cancellationToken);
}
