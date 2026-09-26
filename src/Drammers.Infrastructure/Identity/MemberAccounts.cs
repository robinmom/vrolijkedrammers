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
/// provisioning-saga (Entra-account → lokale gebruiker met rol Lid → welkomstmail). Alleen een exacte match met
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
    IClock clock)
{
    public const string ProvisionMessageType = "account.provision";

    /// <summary>Een tweede identiek verzoek binnen deze tijd maakt geen nieuw verzoek (herhaald tikken, bots).</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record ProvisionMessage(ProvisioningSourceType SourceType, string SourceId);

    // ----- Accountverzoek ("Ik ben al lid") ------------------------------------------------------------------------

    /// <summary>
    /// Legt het verzoek vast. Het zware werk (Graph, mail) gebeurt in de worker, zodat het antwoord en de responstijd
    /// bij een match en een mismatch gelijk zijn (geen enumeratie van leden of e-mailadressen).
    /// </summary>
    public async Task SubmitAccountRequestAsync(string memberNumber, string emailAddress, string? ipAddress, CancellationToken cancellationToken)
    {
        var number = memberNumber.Trim();
        var normalizedEmail = emailAddress.Trim().ToLowerInvariant();
        var now = clock.UtcNow.UtcDateTime;

        var since = now - DuplicateWindow;
        if (await db.AccountRequests.AnyAsync(
                r => r.MemberNumber == number && r.Email == normalizedEmail && r.RequestedAt >= since, cancellationToken))
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

        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.MemberNumber == number, cancellationToken);
        var (status, reason) = member switch
        {
            null => (AccountRequestStatus.Pending, "unknown-member-number"),
            _ when !string.Equals(member.Email?.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase) => (AccountRequestStatus.Pending, "email-mismatch"),
            _ when member.EffectiveStatus != MembershipStatus.Active => (AccountRequestStatus.Pending, "member-not-active"),
            _ when await HasAccountAsync(member.Id, cancellationToken) => (AccountRequestStatus.Duplicate, "has-account"),
            _ => (AccountRequestStatus.Approved, (string?)null),
        };
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

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("account-request.submitted", "AccountRequest", request.Id.ToString(), null,
                JsonSerializer.Serialize(new { status = status.ToString(), reason, memberId = member?.Id }, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
        outbox.Enqueue(ProvisionMessageType, new ProvisionMessage(saga.SourceType, saga.SourceId));
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
            var loginEmail = member.Email?.Trim().ToLowerInvariant()
                ?? throw new DomainException(ErrorCodes.MemberNotEligible, "Het lid heeft geen e-mailadres in e-Boekhouden.", DomainErrorKind.Conflict);

            if (saga.EntraObjectId is null)
            {
                saga.EntraObjectId = await entra.FindByEmailAsync(loginEmail, cancellationToken)
                    ?? await entra.CreateAsync(loginEmail, member.FullName, cancellationToken);
                saga.Step = ProvisioningStep.AccountCreated;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (saga.UserId is null)
            {
                saga.UserId = await CreateOrLinkUserAsync(saga.EntraObjectId, member, loginEmail, cancellationToken);
                saga.Step = ProvisioningStep.MemberCreated;
                await db.SaveChangesAsync(cancellationToken);
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

    private async Task<Guid> CreateOrLinkUserAsync(string objectId, Member member, string loginEmail, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lidRoleId = await db.Roles.Where(r => r.Code == DefaultRoles.Lid).Select(r => r.Id).SingleAsync(cancellationToken);
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.ExternalObjectId == objectId, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = IdGenerator.NewId(),
                ExternalObjectId = objectId,
                Email = loginEmail,
                DisplayName = member.FullName,
                AccountStatus = AccountStatus.Active,
            };
            db.Users.Add(user);
        }

        // Een bestaand account (bijv. een beheerder die ook lid is) wordt gekoppeld en krijgt de rol Lid erbij.
        user.MemberId ??= member.Id;
        if (user.Roles.All(r => r.RoleId != lidRoleId))
        {
            user.Roles.Add(new UserRole { UserId = user.Id, RoleId = lidRoleId, AssignedAt = clock.UtcNow.UtcDateTime });
        }

        user.PermissionsVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        userAccess.Invalidate(objectId);
        return user.Id;
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
            throw new DomainException(ErrorCodes.MemberNotEligible, "Het lid heeft geen e-mailadres in e-Boekhouden. Vul dat daar eerst in en synchroniseer.", DomainErrorKind.Conflict);
        }

        if (await HasAccountAsync(memberId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.MemberHasAccount, "Dit lid heeft al een app-account.", DomainErrorKind.Conflict);
        }
    }

    private Task<bool> HasAccountAsync(Guid memberId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.MemberId == memberId && u.AccountStatus != AccountStatus.Deleted, cancellationToken);

    private async Task<AccountRequest> FindRequestAsync(Guid id, CancellationToken cancellationToken) =>
        await db.AccountRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
        ?? throw new DomainException(ErrorCodes.AccountRequestNotFound, "Accountverzoek niet gevonden.", DomainErrorKind.NotFound);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Concepttekst; de definitieve tekst keurt het bestuur goed (aanvullende DoD fase 9).</summary>
    public static EmailMessage WelcomeMail(string to, string firstName)
    {
        const string Subject = "Welkom in de app van De Vrolijke Drammers";
        var text = $"""
            Beste {firstName},

            Je account voor de app van De Vrolijke Drammers is klaar.

            Zo log je in:
            1. Open de app en kies Meer → Inloggen.
            2. Vul dit e-mailadres in ({to}).
            3. Je krijgt een code per e-mail. Een wachtwoord is niet nodig.

            Heb je dit niet aangevraagd? Neem dan contact op met het secretariaat.

            Alaaf!
            Het bestuur van De Vrolijke Drammers
            """;
        var encodedName = System.Net.WebUtility.HtmlEncode(firstName);
        var encodedTo = System.Net.WebUtility.HtmlEncode(to);
        var html = $"""
            <p>Beste {encodedName},</p>
            <p>Je account voor de app van De Vrolijke Drammers is klaar.</p>
            <p><strong>Zo log je in:</strong></p>
            <ol><li>Open de app en kies <em>Meer → Inloggen</em>.</li><li>Vul dit e-mailadres in ({encodedTo}).</li><li>Je krijgt een code per e-mail. Een wachtwoord is niet nodig.</li></ol>
            <p>Heb je dit niet aangevraagd? Neem dan contact op met het secretariaat.</p>
            <p>Alaaf!<br>Het bestuur van De Vrolijke Drammers</p>
            """;
        return new EmailMessage(to, Subject, text, html);
    }
}

/// <summary>Voert de provisioning uit die via de outbox is aangevraagd (worker).</summary>
public sealed class MemberAccountProvisioningHandler(MemberAccounts accounts) : IOutboxMessageHandler
{
    public string Type => MemberAccounts.ProvisionMessageType;

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken) =>
        accounts.RunProvisioningAsync(JsonSerializer.Deserialize<MemberAccounts.ProvisionMessage>(message.Payload, JsonSerializerOptions.Web)!, cancellationToken);
}
