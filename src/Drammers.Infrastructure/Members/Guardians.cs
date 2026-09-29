using System.Net.Mail;
using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Applications;
using Drammers.Modules.Membership.Guardians;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Notification.Notifications;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

public sealed record GuardianView(
    Guid Id, Guid UserId, string Name, string? Email, GuardianRelationship Relationship, bool IsMember, string? Phone, DateTime CreatedAt);

/// <summary>Voorstel: een lid jonger dan 15 en een ander lid met hetzelfde e-mailadres (fase 17). Alleen een voorstel.</summary>
public sealed record GuardianSuggestion(
    Guid ChildMemberId, string ChildName, string ChildNumber, int? ChildAge, bool ChildDansgarde,
    Guid ParentMemberId, string ParentName, string ParentNumber, int? ParentAge, string Email, Guid? ParentUserId);

public sealed record MemberCandidate(Guid MemberId, string Name, string MemberNumber, int? Age, int Guardians);

public sealed record GuardianRequestView(
    Guid Id, string RequestedByName, string RequestedByEmail, string ChildFirstName, string ChildLastName,
    GuardianRelationship Relationship, string? Phone, GuardianLinkRequestStatus Status, DateTime CreatedAt, DateTime? DecidedAt,
    string? RejectionReason, Guid? MemberId, string? MemberName, IReadOnlyList<MemberCandidate> Candidates);

/// <summary>
/// Eigen account van een lid: vanaf 15 kan het bestuur een eigen account geven (<see cref="AvailableFrom"/>); tot 18 blijven
/// de ouders gekoppeld (<see cref="GuardiansUntil"/>).
/// </summary>
public sealed record OwnAccountInfo(bool HasAccount, string? Email, int? Age, bool CanGetOwnAccount, DateOnly? AvailableFrom, DateOnly? GuardiansUntil, bool Pending);

public sealed record MemberGuardians(
    bool Applies, int Max, IReadOnlyList<GuardianView> Guardians, IReadOnlyList<GuardianSuggestion> Suggestions,
    IReadOnlyList<GuardianRequestView> Requests, OwnAccountInfo OwnAccount);

public sealed record MyChild(
    Guid MemberId, string FullName, string? FirstName, string MemberNumber, DateOnly? BirthDate, int? Age, MembershipStatus Status,
    bool OwnAccount, bool CanShowQr, IReadOnlyList<string> Groups);

public sealed record MyGuardianRequest(Guid Id, string ChildFirstName, string ChildLastName, GuardianLinkRequestStatus Status, DateTime CreatedAt, DateTime? DecidedAt);

/// <summary>
/// Ouders en verzorgers (fase 17): koppelen door het bestuur (bestaand account, nieuwe ouder uitnodigen, voorstel bij
/// hetzelfde e-mailadres), koppelverzoeken uit de app (pas na goedkeuring in het portal) en "Eigen account geven" vanaf 15.
/// Een kind heeft hooguit twee ouders/verzorgers; de koppeling telt tot het kind 18 is.
/// </summary>
public sealed class Guardians(
    DrammersDbContext db,
    MemberAccounts accounts,
    INotificationService notifications,
    IEmailSender email,
    IUserAccessService userAccess,
    IAuditLogger audit,
    ICurrentActor actor,
    IClock clock)
{
    public const int MaxPendingRequestsPerUser = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    public static int? AgeOn(DateOnly? birthDate, DateOnly date)
    {
        if (birthDate is not { } birth)
        {
            return null;
        }

        var age = date.Year - birth.Year;
        return birth > date.AddYears(-age) ? age - 1 : age;
    }

    /// <summary>Telt de koppeling nog? Tot het kind 18 is (zonder geboortedatum: ja).</summary>
    public static bool IsMinor(DateOnly? birthDate, DateOnly today) => AgeOn(birthDate, today) is not { } age || age < GuardianRelation.AdultAge;

    // ----- Portal: per lid ---------------------------------------------------------------------------------------

    public async Task<MemberGuardians> ForMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var member = await FindMemberAsync(memberId, cancellationToken);
        var today = Today;
        var age = AgeOn(member.BirthDate, today);
        var guardians = await GuardiansOfAsync(memberId, cancellationToken);
        var own = await db.Users.AsNoTracking().Where(u => u.MemberId == memberId && u.AccountStatus != AccountStatus.Deleted)
            .Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        var pending = own is null && await db.AccountProvisioning.AnyAsync(
            p => p.SourceId == OwnAccountSource(memberId) && p.CompletedAt == null, cancellationToken);
        var applies = IsMinor(member.BirthDate, today) || guardians.Count > 0;
        var suggestions = applies ? (await SuggestionsAsync(cancellationToken)).Where(s => s.ChildMemberId == memberId).ToList() : [];
        var requests = applies
            ? (await RequestsAsync(GuardianLinkRequestStatus.Pending, cancellationToken)).Where(r => r.Candidates.Any(c => c.MemberId == memberId)).ToList()
            : [];
        var availableFrom = member.BirthDate?.AddYears(MembershipApplication.MinimumAgeOwnAccount);
        var ownInfo = new OwnAccountInfo(
            own is not null, own, age,
            own is null && !pending && age >= MembershipApplication.MinimumAgeOwnAccount && member.EffectiveStatus == MembershipStatus.Active,
            availableFrom, member.BirthDate?.AddYears(GuardianRelation.AdultAge), pending);
        return new MemberGuardians(applies, GuardianRelation.MaxPerChild, guardians, suggestions, requests, ownInfo);
    }

    /// <summary>Koppelt een bestaand account als ouder/verzorger.</summary>
    public async Task LinkAsync(Guid memberId, Guid userId, GuardianRelationship relationship, CancellationToken cancellationToken) =>
        await LinkCoreAsync(memberId, userId, relationship, null, "portal", cancellationToken);

    /// <summary>Nodigt een nieuwe ouder uit (alleen de rol Ouder/verzorger) of koppelt het bestaande account met dat adres.</summary>
    public async Task InviteAsync(Guid memberId, string emailAddress, string name, GuardianRelationship relationship, CancellationToken cancellationToken)
    {
        var address = NormalizeEmail(emailAddress);
        var displayName = Required(name, "de naam van de ouder of verzorger", 100);
        var child = await FindMemberAsync(memberId, cancellationToken);
        await EnsureCanAddAsync(child, cancellationToken);
        var existed = await db.Users.AnyAsync(u => u.Email == address && u.AccountStatus != AccountStatus.Deleted, cancellationToken);
        var userId = await accounts.EnsureAccountAsync(address, displayName, null, DefaultRoles.Ouder, cancellationToken);
        await LinkCoreAsync(memberId, userId, relationship, null, "invite", cancellationToken);
        if (!existed)
        {
            await email.SendAsync(MembershipApplications.GuardianWelcomeMail(address, displayName, child.FirstName ?? child.FullName), cancellationToken);
        }
    }

    public async Task UnlinkAsync(Guid memberId, Guid relationId, CancellationToken cancellationToken)
    {
        var relation = await db.GuardianRelations.SingleOrDefaultAsync(g => g.Id == relationId && g.MemberId == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GuardianNotFound, "Deze koppeling bestaat niet (meer).", DomainErrorKind.NotFound);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.GuardianRelations.Remove(relation);
        await db.SaveChangesAsync(cancellationToken);
        await DropParentRoleIfUnusedAsync(relation.GuardianUserId, cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("guardian.unlinked", "Member", memberId.ToString(), null, JsonSerializer.Serialize(new { userId = relation.GuardianUserId }, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ----- Voorstellen: zelfde e-mailadres -----------------------------------------------------------------------

    /// <summary>
    /// Leden jonger dan 15 en een ander lid (niet jonger dan 15) met hetzelfde e-mailadres, zonder bestaande koppeling en
    /// niet eerder afgewezen met "Geen relatie". Er wordt niets vanzelf gekoppeld.
    /// </summary>
    public async Task<IReadOnlyList<GuardianSuggestion>> SuggestionsAsync(CancellationToken cancellationToken)
    {
        var today = Today;
        var members = await db.Members.AsNoTracking()
            .Where(m => m.Email != null && m.Email != "" && (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active)
            .Select(m => new { m.Id, m.FullName, m.MemberNumber, m.BirthDate, m.Email, m.ParadeGroupName })
            .ToListAsync(cancellationToken);
        var groups = members.GroupBy(m => m.Email!.Trim().ToLowerInvariant()).Where(g => g.Count() > 1).ToList();
        if (groups.Count == 0)
        {
            return [];
        }

        var dismissed = (await db.GuardianSuggestionDismissals.AsNoTracking().Select(d => new { d.ChildMemberId, d.ParentMemberId }).ToListAsync(cancellationToken))
            .Select(d => (d.ChildMemberId, d.ParentMemberId)).ToHashSet();
        var relations = (await db.GuardianRelations.AsNoTracking().Select(g => new { g.MemberId, g.GuardianUserId }).ToListAsync(cancellationToken))
            .ToList();
        var linked = relations.Select(r => (r.MemberId, r.GuardianUserId)).ToHashSet();
        var guardianCount = relations.GroupBy(r => r.MemberId).ToDictionary(g => g.Key, g => g.Count());
        var emails = groups.Select(g => g.Key).ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.AccountStatus != AccountStatus.Deleted && (u.MemberId != null || emails.Contains(u.Email)))
            .Select(u => new { u.Id, u.MemberId, u.Email })
            .ToListAsync(cancellationToken);

        var result = new List<GuardianSuggestion>();
        foreach (var group in groups)
        {
            foreach (var child in group.Where(m => AgeOn(m.BirthDate, today) is { } a && a < MembershipApplication.MinimumAgeOwnAccount))
            {
                if (guardianCount.GetValueOrDefault(child.Id) >= GuardianRelation.MaxPerChild)
                {
                    continue;
                }

                foreach (var parent in group.Where(m => m.Id != child.Id && !(AgeOn(m.BirthDate, today) is { } pa && pa < MembershipApplication.MinimumAgeOwnAccount)))
                {
                    var parentUser = users.FirstOrDefault(u => u.MemberId == parent.Id)?.Id
                        ?? users.FirstOrDefault(u => string.Equals(u.Email, group.Key, StringComparison.OrdinalIgnoreCase))?.Id;
                    if (dismissed.Contains((child.Id, parent.Id)) || (parentUser is { } pu && linked.Contains((child.Id, pu))))
                    {
                        continue;
                    }

                    result.Add(new GuardianSuggestion(
                        child.Id, child.FullName, child.MemberNumber, AgeOn(child.BirthDate, today), Dansgarde.IsDansgarde(child.ParadeGroupName),
                        parent.Id, parent.FullName, parent.MemberNumber, AgeOn(parent.BirthDate, today), group.Key, parentUser));
                }
            }
        }

        return [.. result.OrderBy(s => s.ChildName, StringComparer.CurrentCulture).ThenBy(s => s.ParentName, StringComparer.CurrentCulture)];
    }

    /// <summary>
    /// Het bestuur bevestigt een voorstel. Heeft het andere lid nog geen app-account, dan krijgt het er een (rol Lid) en een
    /// welkomstmail.
    /// </summary>
    public async Task AcceptSuggestionAsync(Guid childMemberId, Guid parentMemberId, CancellationToken cancellationToken)
    {
        var suggestion = (await SuggestionsAsync(cancellationToken)).FirstOrDefault(s => s.ChildMemberId == childMemberId && s.ParentMemberId == parentMemberId)
            ?? throw new DomainException(ErrorCodes.GuardianNotFound, "Dit voorstel bestaat niet (meer).", DomainErrorKind.NotFound);
        var userId = suggestion.ParentUserId;
        if (userId is null)
        {
            var parent = await FindMemberAsync(parentMemberId, cancellationToken);
            userId = await accounts.EnsureAccountAsync(suggestion.Email, parent.FullName, parentMemberId, DefaultRoles.Lid, cancellationToken);
            await email.SendAsync(MemberAccounts.WelcomeMail(suggestion.Email, parent.FirstName ?? parent.FullName), cancellationToken);
        }

        await LinkCoreAsync(childMemberId, userId.Value, GuardianRelationship.Parent, null, "suggestion", cancellationToken);
    }

    public async Task DismissSuggestionAsync(Guid childMemberId, Guid parentMemberId, CancellationToken cancellationToken)
    {
        if (await db.GuardianSuggestionDismissals.AnyAsync(d => d.ChildMemberId == childMemberId && d.ParentMemberId == parentMemberId, cancellationToken))
        {
            return;
        }

        await FindMemberAsync(childMemberId, cancellationToken);
        await FindMemberAsync(parentMemberId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.GuardianSuggestionDismissals.Add(new GuardianSuggestionDismissal
        {
            ChildMemberId = childMemberId,
            ParentMemberId = parentMemberId,
            DismissedAt = clock.UtcNow.UtcDateTime,
            DismissedBy = actor.UserId,
        });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("guardian.suggestion-dismissed", "Member", childMemberId.ToString(), null, JsonSerializer.Serialize(new { parentMemberId }, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ----- Koppelverzoeken uit de app ----------------------------------------------------------------------------

    public async Task<Guid> SubmitRequestAsync(
        Guid userId, string childFirstName, string childLastName, GuardianRelationship relationship, string? phone, CancellationToken cancellationToken)
    {
        var first = Required(childFirstName, "de voornaam van je kind", 50);
        var last = Required(childLastName, "de achternaam van je kind", 80);
        var pending = await db.GuardianLinkRequests.CountAsync(r => r.RequestedByUserId == userId && r.Status == GuardianLinkRequestStatus.Pending, cancellationToken);
        if (pending >= MaxPendingRequestsPerUser)
        {
            throw Invalid("Je hebt al een paar verzoeken die op het bestuur wachten. Wacht tot die zijn bekeken.");
        }

        if (await db.GuardianLinkRequests.AnyAsync(
                r => r.RequestedByUserId == userId && r.Status == GuardianLinkRequestStatus.Pending && r.ChildFirstName == first && r.ChildLastName == last,
                cancellationToken))
        {
            throw new DomainException(ErrorCodes.GuardianExists, "Je hebt voor dit kind al een verzoek gedaan dat op het bestuur wacht.", DomainErrorKind.Conflict);
        }

        var request = new GuardianLinkRequest
        {
            Id = IdGenerator.NewId(),
            RequestedByUserId = userId,
            ChildFirstName = first,
            ChildLastName = last,
            Relationship = relationship,
            Phone = string.IsNullOrWhiteSpace(phone) ? null : Required(phone, "het telefoonnummer", 30),
            Status = GuardianLinkRequestStatus.Pending,
            CreatedAt = clock.UtcNow.UtcDateTime,
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.GuardianLinkRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("guardian.requested", "GuardianLinkRequest", request.Id.ToString(), null, null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return request.Id;
    }

    public async Task<IReadOnlyList<MyGuardianRequest>> MyRequestsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.GuardianLinkRequests.AsNoTracking().Where(r => r.RequestedByUserId == userId)
            .OrderByDescending(r => r.CreatedAt).Take(20)
            .Select(r => new MyGuardianRequest(r.Id, r.ChildFirstName, r.ChildLastName, r.Status, r.CreatedAt, r.DecidedAt))
            .ToListAsync(cancellationToken);

    /// <summary>Verzoeken met de leden die op de opgegeven naam lijken (het bestuur kiest).</summary>
    public async Task<IReadOnlyList<GuardianRequestView>> RequestsAsync(GuardianLinkRequestStatus? status, CancellationToken cancellationToken)
    {
        var query = db.GuardianLinkRequests.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        var rows = await query.OrderByDescending(r => r.CreatedAt).Take(200)
            .Join(db.Users, r => r.RequestedByUserId, u => u.Id, (r, u) => new { r, u.DisplayName, u.Email })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var today = Today;
        var members = await db.Members.AsNoTracking()
            .Where(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active)
            .Select(m => new { m.Id, m.FullName, m.FirstName, m.LastName, m.MemberNumber, m.BirthDate })
            .ToListAsync(cancellationToken);
        var counts = (await db.GuardianRelations.AsNoTracking().GroupBy(g => g.MemberId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .ToDictionary(g => g.Key, g => g.Count);
        var names = members.ToDictionary(m => m.Id, m => m.FullName);

        return [.. rows.Select(row =>
        {
            var first = Normalize(row.r.ChildFirstName);
            var last = Normalize(row.r.ChildLastName);
            var candidates = members
                .Where(m => Normalize(m.FirstName ?? m.FullName.Split(' ')[0]) == first
                    && (Normalize(m.LastName) == last || Normalize(m.FullName).EndsWith(" " + last, StringComparison.Ordinal)))
                .OrderBy(m => m.FullName, StringComparer.CurrentCulture)
                .Select(m => new MemberCandidate(m.Id, m.FullName, m.MemberNumber, AgeOn(m.BirthDate, today), counts.GetValueOrDefault(m.Id)))
                .Take(10).ToList();
            return new GuardianRequestView(
                row.r.Id, row.DisplayName, row.Email, row.r.ChildFirstName, row.r.ChildLastName, row.r.Relationship, row.r.Phone,
                row.r.Status, row.r.CreatedAt, row.r.DecidedAt, row.r.RejectionReason, row.r.MemberId,
                row.r.MemberId is { } id ? names.GetValueOrDefault(id) : null, candidates);
        })];
    }

    public async Task ApproveRequestAsync(Guid requestId, Guid memberId, CancellationToken cancellationToken)
    {
        var request = await FindPendingRequestAsync(requestId, cancellationToken);
        var child = await FindMemberAsync(memberId, cancellationToken);
        await LinkCoreAsync(memberId, request.RequestedByUserId, request.Relationship, request.Phone, "request", cancellationToken, notify: false);
        request.Status = GuardianLinkRequestStatus.Approved;
        request.MemberId = memberId;
        request.DecidedAt = clock.UtcNow.UtcDateTime;
        request.DecidedBy = actor.UserId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await notifications.EnqueueAsync(new SystemNotification(
            "Koppeling goedgekeurd",
            $"{child.FirstName ?? child.FullName} staat nu onder Mijn kinderen.",
            NotificationCategory.System, new NotificationAudience(UserIds: [request.RequestedByUserId]), "drammers://kinderen",
            "GuardianRequest", request.Id), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("guardian.request-approved", "GuardianLinkRequest", request.Id.ToString(), null, JsonSerializer.Serialize(new { memberId }, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RejectRequestAsync(Guid requestId, string? reason, CancellationToken cancellationToken)
    {
        var request = await FindPendingRequestAsync(requestId, cancellationToken);
        request.Status = GuardianLinkRequestStatus.Rejected;
        request.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
        request.DecidedAt = clock.UtcNow.UtcDateTime;
        request.DecidedBy = actor.UserId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await notifications.EnqueueAsync(new SystemNotification(
            "Koppelverzoek niet goedgekeurd",
            $"Je verzoek voor {request.ChildFirstName} {request.ChildLastName} is niet goedgekeurd. Vragen? Neem contact op met het secretariaat.",
            NotificationCategory.System, new NotificationAudience(UserIds: [request.RequestedByUserId]), "drammers://kinderen",
            "GuardianRequest", request.Id), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("guardian.request-rejected", "GuardianLinkRequest", request.Id.ToString(), null, JsonSerializer.Serialize(new { reason = request.RejectionReason }, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ----- Eigen account vanaf 15 --------------------------------------------------------------------------------

    /// <summary>
    /// "Eigen account geven": het kind (15+) krijgt een account op het eigen e-mailadres. De ouders blijven gekoppeld tot
    /// 18 (meldingen), maar tonen de QR niet meer. Het e-mailadres in e-Boekhouden verandert niet.
    /// </summary>
    public async Task GiveOwnAccountAsync(Guid memberId, string emailAddress, CancellationToken cancellationToken)
    {
        var member = await FindMemberAsync(memberId, cancellationToken);
        var address = NormalizeEmail(emailAddress);
        if (AgeOn(member.BirthDate, Today) is not { } age || age < MembershipApplication.MinimumAgeOwnAccount)
        {
            throw new DomainException(ErrorCodes.OwnAccountNotAllowed,
                $"Een eigen account kan vanaf {MembershipApplication.MinimumAgeOwnAccount} jaar (vul zo nodig de geboortedatum in).", DomainErrorKind.Conflict);
        }

        if (await db.Users.AnyAsync(u => u.Email == address && u.AccountStatus != AccountStatus.Deleted, cancellationToken))
        {
            throw new DomainException(ErrorCodes.EmailInUse,
                "Dit e-mailadres hoort al bij een ander app-account. Gebruik het eigen e-mailadres van het lid.", DomainErrorKind.Conflict);
        }

        await accounts.ProvisionOwnAccountAsync(memberId, address, cancellationToken);
    }

    public static string OwnAccountSource(Guid memberId) => $"own:{memberId}";

    // ----- App: ouder ----------------------------------------------------------------------------------------------

    /// <summary>Kinderen van een ouder (tot 18). De QR toont de ouder alleen zolang het kind geen eigen account heeft.</summary>
    public async Task<IReadOnlyList<MyChild>> ChildrenOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        var today = Today;
        var children = await db.GuardianRelations.AsNoTracking().Where(g => g.GuardianUserId == userId)
            .Join(db.Members, g => g.MemberId, m => m.Id, (g, m) => m)
            .ToListAsync(cancellationToken);
        children = [.. children.Where(m => IsMinor(m.BirthDate, today))];
        if (children.Count == 0)
        {
            return [];
        }

        var ids = children.Select(c => c.Id).ToList();
        var withAccount = (await db.Users.AsNoTracking().Where(u => u.MemberId != null && ids.Contains(u.MemberId.Value) && u.AccountStatus != AccountStatus.Deleted)
            .Select(u => u.MemberId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var groups = (await db.GroupMemberships.AsNoTracking()
            .Where(gm => ids.Contains(gm.MemberId) && (gm.ValidFrom == null || gm.ValidFrom <= today) && (gm.ValidTo == null || gm.ValidTo >= today))
            .Join(db.Groups.Where(g => g.Active), gm => gm.GroupId, g => g.Id, (gm, g) => new { gm.MemberId, g.Name })
            .ToListAsync(cancellationToken)).ToLookup(g => g.MemberId, g => g.Name);
        return [.. children.OrderBy(c => c.FullName, StringComparer.CurrentCulture).Select(c => new MyChild(
            c.Id, c.FullName, c.FirstName, c.MemberNumber, c.BirthDate, AgeOn(c.BirthDate, today), c.EffectiveStatus,
            withAccount.Contains(c.Id), !withAccount.Contains(c.Id) && c.EffectiveStatus == MembershipStatus.Active,
            [.. groups[c.Id].OrderBy(n => n, StringComparer.CurrentCulture)]))];
    }

    // ----- Intern ------------------------------------------------------------------------------------------------

    private async Task LinkCoreAsync(
        Guid memberId, Guid userId, GuardianRelationship relationship, string? phone, string source, CancellationToken cancellationToken, bool notify = true)
    {
        var child = await FindMemberAsync(memberId, cancellationToken);
        await EnsureCanAddAsync(child, cancellationToken);
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == userId && u.AccountStatus != AccountStatus.Deleted, cancellationToken)
            ?? throw new DomainException(ErrorCodes.UserNotFound, "Account niet gevonden.", DomainErrorKind.NotFound);
        if (user.MemberId == memberId)
        {
            throw new DomainException(ErrorCodes.GuardianNotAllowed, "Een lid kan niet zijn eigen ouder of verzorger zijn.", DomainErrorKind.Conflict);
        }

        if (await db.GuardianRelations.AnyAsync(g => g.MemberId == memberId && g.GuardianUserId == userId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.GuardianExists, $"{user.DisplayName} is al gekoppeld aan {child.FullName}.", DomainErrorKind.Conflict);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        db.GuardianRelations.Add(new GuardianRelation
        {
            Id = IdGenerator.NewId(),
            MemberId = memberId,
            GuardianUserId = userId,
            GuardianName = user.DisplayName.Length > 100 ? user.DisplayName[..100] : user.DisplayName,
            GuardianPhone = phone,
            Relationship = relationship,
            VerifiedAt = now,
            CreatedBy = actor.UserId,
            CreatedAt = now,
        });
        var roleId = await db.Roles.Where(r => r.Code == DefaultRoles.Ouder).Select(r => r.Id).SingleAsync(cancellationToken);
        if (user.Roles.All(r => r.RoleId != roleId))
        {
            user.Roles.Add(new UserRole { UserId = user.Id, RoleId = roleId, AssignedAt = now, AssignedBy = actor.UserId });
            user.PermissionsVersion++;
        }

        if (notify)
        {
            await notifications.EnqueueAsync(new SystemNotification(
                "Gekoppeld als ouder/verzorger",
                $"{child.FirstName ?? child.FullName} staat nu onder Mijn kinderen in de app.",
                NotificationCategory.System, new NotificationAudience(UserIds: [userId]), "drammers://kinderen"), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("guardian.linked", "Member", memberId.ToString(), null,
                JsonSerializer.Serialize(new { userId, relationship = relationship.ToString(), source }, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        userAccess.Invalidate(user.ExternalObjectId);
    }

    private async Task EnsureCanAddAsync(Member child, CancellationToken cancellationToken)
    {
        if (!IsMinor(child.BirthDate, Today))
        {
            throw new DomainException(ErrorCodes.GuardianNotAllowed, "Ouders of verzorgers koppel je alleen aan leden jonger dan 18.", DomainErrorKind.Conflict);
        }

        if (await db.GuardianRelations.CountAsync(g => g.MemberId == child.Id, cancellationToken) >= GuardianRelation.MaxPerChild)
        {
            throw new DomainException(ErrorCodes.GuardianLimit,
                $"{child.FullName} heeft al {GuardianRelation.MaxPerChild} ouders/verzorgers. Ontkoppel er eerst een.", DomainErrorKind.Conflict);
        }
    }

    private async Task DropParentRoleIfUnusedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await db.GuardianRelations.AnyAsync(g => g.GuardianUserId == userId, cancellationToken))
        {
            return;
        }

        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        var roleId = await db.Roles.Where(r => r.Code == DefaultRoles.Ouder).Select(r => r.Id).SingleAsync(cancellationToken);
        if (user?.Roles.FirstOrDefault(r => r.RoleId == roleId) is { } role && user.Roles.Count > 1)
        {
            user.Roles.Remove(role);
            user.PermissionsVersion++;
            await db.SaveChangesAsync(cancellationToken);
            userAccess.Invalidate(user.ExternalObjectId);
        }
    }

    private async Task<IReadOnlyList<GuardianView>> GuardiansOfAsync(Guid memberId, CancellationToken cancellationToken) =>
        await db.GuardianRelations.AsNoTracking().Where(g => g.MemberId == memberId).OrderBy(g => g.CreatedAt)
            .Join(db.Users, g => g.GuardianUserId, u => u.Id, (g, u) => new GuardianView(
                g.Id, u.Id, u.DisplayName, u.Email, g.Relationship, u.MemberId != null, g.GuardianPhone, g.CreatedAt))
            .ToListAsync(cancellationToken);

    private async Task<Member> FindMemberAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
        ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);

    private async Task<GuardianLinkRequest> FindPendingRequestAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.GuardianLinkRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GuardianRequestNotFound, "Koppelverzoek niet gevonden.", DomainErrorKind.NotFound);
        return request.Status == GuardianLinkRequestStatus.Pending
            ? request
            : throw new DomainException(ErrorCodes.GuardianRequestDecided, "Dit verzoek is al afgehandeld.", DomainErrorKind.Conflict);
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeEmail(string? value)
    {
        var address = (value ?? string.Empty).Trim().ToLowerInvariant();
        return address.Length is > 0 and <= 254 && MailAddress.TryCreate(address, out _) ? address : throw Invalid("Het e-mailadres is ongeldig.");
    }

    private static string Required(string? value, string what, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw Invalid($"Vul {what} in.");
        }

        return trimmed.Length <= max ? trimmed : throw Invalid($"Maak {what} korter (hooguit {max} tekens).");
    }

    private static DomainException Invalid(string message) => new(ErrorCodes.Validation, message);
}
