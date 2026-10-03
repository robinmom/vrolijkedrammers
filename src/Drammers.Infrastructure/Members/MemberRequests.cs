using System.Net;
using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

/// <summary>IBAN's van leden versleuteld opslaan (Data Protection, sleutelring in Blob en Key Vault; docs/06).</summary>
public sealed class MemberIbanProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Drammers.MemberIban.v1");

    public string Protect(string iban) => _protector.Protect(iban);

    public string Unprotect(string protectedIban) => _protector.Unprotect(protectedIban);

    public static string Mask(string? last4) => last4 is null ? "—" : $"**** {last4}";
}

/// <summary>Wat een lid wil wijzigen; leeg (null) = ongewijzigd. Bij een IBAN hoort een machtiging.</summary>
public sealed record MemberChangeInput(
    string? AddressLine, string? PostalCode, string? City, string? Email, string? Phone, string? MobilePhone, string? Iban, string? AccountHolder,
    bool MandateConsent);

/// <summary>Bankgegevens bij het akkoord op het verbreken (alleen voor wie zelf gaat betalen).</summary>
public sealed record BankInput(string? Iban, string? AccountHolder, bool MandateConsent);

public sealed record MyChangeRequest(Guid Id, DateTime RequestedAt, MemberRequestStatus Status, IReadOnlyList<string> Fields, string? RejectionReason);

public sealed record MyCombination(
    string Role, Guid OtherMemberId, string OtherName, Guid? BreakRequestId, CombinationBreakStatus? BreakStatus, bool IAgreed, bool OtherAgreed,
    bool IbanRequiredFromMe);

public sealed record MyMemberRequests(MyChangeRequest? LatestChange, MyCombination? Combination);

public sealed record ChangeRequestField(string Field, string Label, string? Current, string? Requested);

public sealed record ChangeRequestItem(
    Guid Id, Guid MemberId, string MemberNumber, string FullName, DateTime RequestedAt, IReadOnlyList<ChangeRequestField> Fields);

public sealed record BreakRequestItem(
    Guid Id, Guid PayerMemberId, string PayerName, Guid PartnerMemberId, string PartnerName, string InitiatedBy, DateTime InitiatedAt,
    CombinationBreakStatus Status, DateTime? PayerAgreedAt, DateTime? PartnerAgreedAt, string? PartnerIbanMasked, string? PartnerAccountHolder);

public sealed record MemberRequestsOverview(IReadOnlyList<ChangeRequestItem> Changes, IReadOnlyList<BreakRequestItem> Breaks);

/// <summary>
/// Wijzigingsverzoeken en het verbreken van combinaties (fase 26). Leden doen een verzoek in de app; niets wordt
/// doorgevoerd zonder goedkeuring van de ledenadministratie in het portal (<c>member.update</c>). Goedgekeurde wijzigingen
/// worden "handmatig" (fase 24): de sync met e-Boekhouden overschrijft ze niet.
/// </summary>
public sealed class MemberRequests(
    DrammersDbContext db, MemberAdministration members, MemberIbanProtector ibans, IEmailSender email, IAuditLogger audit, IClock clock,
    ICurrentActor actor)
{
    private const string ReplyTo = "secretaris@vrolijkedrammers.nl";

    // ----- Lid (app) ------------------------------------------------------------------------------------------------

    public async Task<MyMemberRequests> MineAsync(Guid memberId, CancellationToken cancellationToken)
    {
        // Nieuwste eerst; bij gelijke tijd beslist het (tijd-geordende) UUIDv7-id. SQL Server sorteert GUID's anders, dus in het geheugen.
        var latest = (await db.MemberChangeRequests.AsNoTracking().Where(r => r.MemberId == memberId).ToListAsync(cancellationToken))
            .OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.Id).FirstOrDefault();
        MyChangeRequest? change = latest is null || (latest.Status != MemberRequestStatus.Pending && latest.DecidedAt < clock.UtcNow.UtcDateTime.AddDays(-30))
            ? null
            : new MyChangeRequest(latest.Id, latest.RequestedAt, latest.Status, ChangedFields(latest), latest.RejectionReason);
        return new MyMemberRequests(change, await CombinationAsync(memberId, cancellationToken));
    }

    private async Task<MyCombination?> CombinationAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var me = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId, cancellationToken);
        var (payer, partner) = await PairAsync(me, cancellationToken);
        if (payer is null || partner is null)
        {
            return null;
        }

        var iAmPayer = me.Id == payer.Id;
        var other = iAmPayer ? partner : payer;
        var request = await OpenBreakAsync(payer.Id, cancellationToken);
        var iAgreed = request is not null && (iAmPayer ? request.PayerAgreedAt : request.PartnerAgreedAt) is not null;
        var otherAgreed = request is not null && (iAmPayer ? request.PartnerAgreedAt : request.PayerAgreedAt) is not null;
        return new MyCombination(iAmPayer ? "Payer" : "Partner", other.Id, other.FullName, request?.Id, request?.Status, iAgreed, otherAgreed,
            IbanRequiredFromMe: !iAmPayer);
    }

    /// <summary>Gegevens wijzigen: een nieuw verzoek vervangt een openstaand verzoek van hetzelfde lid.</summary>
    public async Task<Guid> SubmitChangeAsync(Guid memberId, Guid? userId, MemberChangeInput input, CancellationToken cancellationToken)
    {
        var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId, cancellationToken);
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        static string? Changed(string? requested, string? current) =>
            requested is { } r && !string.Equals(r, current?.Trim(), StringComparison.OrdinalIgnoreCase) ? r : null;

        var request = new MemberChangeRequest
        {
            Id = IdGenerator.NewId(),
            MemberId = memberId,
            RequestedBy = userId,
            RequestedAt = clock.UtcNow.UtcDateTime,
            Status = MemberRequestStatus.Pending,
            AddressLine = Changed(Clean(input.AddressLine), member.AddressLine),
            PostalCode = Changed(Clean(input.PostalCode)?.ToUpperInvariant(), member.PostalCode),
            City = Changed(Clean(input.City), member.City),
            Email = Changed(Clean(input.Email)?.ToLowerInvariant(), member.Email),
            Phone = Changed(Clean(input.Phone), member.Phone),
            MobilePhone = Changed(Clean(input.MobilePhone), member.MobilePhone),
        };

        if (request.Email is { } newEmail && !System.Net.Mail.MailAddress.TryCreate(newEmail, out _))
        {
            throw new DomainException(ErrorCodes.Validation, "Het e-mailadres is ongeldig.");
        }

        if (Clean(input.Iban) is { } iban)
        {
            var bank = Bank(iban, input.AccountHolder, input.MandateConsent);
            (request.IbanProtected, request.IbanLast4, request.AccountHolder, request.MandateConsentAt) = (bank.Protected, bank.Last4, bank.Holder, request.RequestedAt);
        }

        if (ChangedFields(request).Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Er is niets gewijzigd.");
        }

        await db.MemberChangeRequests.Where(r => r.MemberId == memberId && r.Status == MemberRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, MemberRequestStatus.Cancelled), cancellationToken);
        db.MemberChangeRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.change-requested", "Member", memberId.ToString(), null,
            JsonSerializer.Serialize(new { fields = ChangedFields(request) }, JsonSerializerOptions.Web)), cancellationToken);
        return request.Id;
    }

    public async Task CancelChangeAsync(Guid memberId, Guid requestId, CancellationToken cancellationToken)
    {
        var updated = await db.MemberChangeRequests.Where(r => r.Id == requestId && r.MemberId == memberId && r.Status == MemberRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, MemberRequestStatus.Cancelled), cancellationToken);
        if (updated == 0)
        {
            throw new DomainException(ErrorCodes.NotFound, "Er is geen openstaand verzoek.", DomainErrorKind.NotFound);
        }
    }

    /// <summary>Verbreken aanvragen; telt meteen als akkoord van de aanvrager. Het tweede lid geeft hierbij zijn IBAN.</summary>
    public async Task StartBreakAsync(Guid memberId, BankInput bank, CancellationToken cancellationToken)
    {
        var me = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId, cancellationToken);
        var (payer, partner) = await PairAsync(me, cancellationToken);
        if (payer is null || partner is null)
        {
            throw new DomainException(ErrorCodes.Validation, "Je lidmaatschap is geen combinatie.");
        }

        if (await OpenBreakAsync(payer.Id, cancellationToken) is not null)
        {
            throw new DomainException(ErrorCodes.Validation, "Er loopt al een verzoek om de combinatie te verbreken.", DomainErrorKind.Conflict);
        }

        var now = clock.UtcNow.UtcDateTime;
        var request = new CombinationBreakRequest
        {
            Id = IdGenerator.NewId(),
            PayerMemberId = payer.Id,
            PartnerMemberId = partner.Id,
            InitiatedByMemberId = me.Id,
            InitiatedAt = now,
            Status = CombinationBreakStatus.AwaitingAgreement,
        };
        Agree(request, me.Id == payer.Id, bank, now);
        db.CombinationBreakRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.combination-break-requested", "Member", payer.Id.ToString(), null,
            JsonSerializer.Serialize(new { partner = partner.Id, initiatedBy = me.Id }, JsonSerializerOptions.Web)), cancellationToken);

        var other = me.Id == payer.Id ? partner : payer;
        if (other.Email is { Length: > 0 } address)
        {
            await email.SendAsync(Mail(address, other.FirstName ?? other.FullName, "Verzoek om jullie combinatielidmaatschap te verbreken",
                $"{me.FullName} wil jullie combinatielidmaatschap verbreken, zodat ieder een eigen lidmaatschap heeft. Ben je het ermee eens? Geef dan akkoord in de app onder Meer → Mijn gegevens. Daarna beoordeelt de ledenadministratie het verzoek."),
                cancellationToken);
        }
    }

    /// <summary>Akkoord van de ander; daarmee gaat het verzoek naar de ledenadministratie.</summary>
    public async Task AgreeBreakAsync(Guid memberId, BankInput bank, CancellationToken cancellationToken)
    {
        var me = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId, cancellationToken);
        var (payer, _) = await PairAsync(me, cancellationToken);
        var request = payer is null ? null : await db.CombinationBreakRequests.SingleOrDefaultAsync(
            r => r.PayerMemberId == payer.Id && r.Status == CombinationBreakStatus.AwaitingAgreement, cancellationToken);
        if (request is null)
        {
            throw new DomainException(ErrorCodes.NotFound, "Er is geen verzoek om akkoord op te geven.", DomainErrorKind.NotFound);
        }

        var iAmPayer = me.Id == request.PayerMemberId;
        if ((iAmPayer ? request.PayerAgreedAt : request.PartnerAgreedAt) is not null)
        {
            throw new DomainException(ErrorCodes.Validation, "Je hebt al akkoord gegeven; nu is de ander aan de beurt.", DomainErrorKind.Conflict);
        }

        Agree(request, iAmPayer, bank, clock.UtcNow.UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.combination-break-agreed", "Member", request.PayerMemberId.ToString(), null,
            JsonSerializer.Serialize(new { by = me.Id }, JsonSerializerOptions.Web)), cancellationToken);
    }

    /// <summary>Niet akkoord of toch niet: beide leden kunnen een lopend verzoek intrekken.</summary>
    public async Task CancelBreakAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var me = await db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId, cancellationToken);
        var (payer, _) = await PairAsync(me, cancellationToken);
        var request = (payer is null ? null : await OpenBreakTrackedAsync(payer.Id, cancellationToken))
            ?? throw new DomainException(ErrorCodes.NotFound, "Er loopt geen verzoek.", DomainErrorKind.NotFound);
        request.Status = CombinationBreakStatus.Cancelled;
        request.DecidedAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.combination-break-cancelled", "Member", request.PayerMemberId.ToString(), null,
            JsonSerializer.Serialize(new { by = me.Id }, JsonSerializerOptions.Web)), cancellationToken);
    }

    // ----- Ledenadministratie (portal) ------------------------------------------------------------------------------

    public async Task<MemberRequestsOverview> OverviewAsync(CancellationToken cancellationToken)
    {
        var pending = await db.MemberChangeRequests.AsNoTracking().Where(r => r.Status == MemberRequestStatus.Pending)
            .OrderBy(r => r.RequestedAt).ToListAsync(cancellationToken);
        var breaks = await db.CombinationBreakRequests.AsNoTracking()
            .Where(r => r.Status == CombinationBreakStatus.AwaitingAgreement || r.Status == CombinationBreakStatus.AwaitingApproval)
            .OrderBy(r => r.InitiatedAt).ToListAsync(cancellationToken);
        var ids = pending.Select(r => r.MemberId).Concat(breaks.SelectMany(b => new[] { b.PayerMemberId, b.PartnerMemberId })).Distinct().ToList();
        var byId = await db.Members.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);

        var changes = pending.Where(r => byId.ContainsKey(r.MemberId)).Select(r =>
        {
            var m = byId[r.MemberId];
            var fields = new List<ChangeRequestField>();
            void Add(string field, string label, string? current, string? requested)
            {
                if (requested is not null)
                {
                    fields.Add(new ChangeRequestField(field, label, current, requested));
                }
            }

            Add("address", "Adres", m.AddressLine, r.AddressLine);
            Add("postalCode", "Postcode", m.PostalCode, r.PostalCode);
            Add("city", "Plaats", m.City, r.City);
            Add("email", "E-mailadres", m.Email, r.Email);
            Add("phone", "Telefoon", m.Phone, r.Phone);
            Add("mobilePhone", "Mobiel", m.MobilePhone, r.MobilePhone);
            if (r.IbanProtected is not null)
            {
                fields.Add(new ChangeRequestField("iban", "IBAN", m.IbanLast4 is null ? null : MemberIbanProtector.Mask(m.IbanLast4),
                    $"{MemberIbanProtector.Mask(r.IbanLast4)} ({r.AccountHolder}), machtiging gegeven"));
            }

            return new ChangeRequestItem(r.Id, m.Id, m.MemberNumber, m.FullName, r.RequestedAt, fields);
        }).ToList();

        var breakItems = breaks.Where(b => byId.ContainsKey(b.PayerMemberId) && byId.ContainsKey(b.PartnerMemberId)).Select(b =>
            new BreakRequestItem(b.Id, b.PayerMemberId, byId[b.PayerMemberId].FullName, b.PartnerMemberId, byId[b.PartnerMemberId].FullName,
                byId[b.InitiatedByMemberId].FullName, b.InitiatedAt, b.Status, b.PayerAgreedAt, b.PartnerAgreedAt,
                b.PartnerIbanLast4 is null ? null : MemberIbanProtector.Mask(b.PartnerIbanLast4), b.PartnerAccountHolder)).ToList();
        return new MemberRequestsOverview(changes, breakItems);
    }

    /// <summary>Wijziging doorvoeren: de velden worden "handmatig" (de sync overschrijft ze niet); een IBAN met nieuwe machtiging.</summary>
    public async Task ApproveChangeAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await db.MemberChangeRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.Status == MemberRequestStatus.Pending, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Verzoek niet gevonden of al afgehandeld.", DomainErrorKind.NotFound);
        var m = await db.Members.AsNoTracking().SingleAsync(x => x.Id == request.MemberId, cancellationToken);
        await members.UpdateDataAsync(m.Id, new MemberDataUpdate(m.FullName, m.Salutation, m.Gender, request.AddressLine ?? m.AddressLine,
            request.PostalCode ?? m.PostalCode, request.City ?? m.City, m.Country, request.Email ?? m.Email, request.Phone ?? m.Phone,
            request.MobilePhone ?? m.MobilePhone, m.BirthDate, m.JoinYear, m.MemberCategory, m.ParadeGroupName), cancellationToken);

        var member = await db.Members.SingleAsync(x => x.Id == request.MemberId, cancellationToken);
        if (request.IbanProtected is not null)
        {
            SetBank(member, request.IbanProtected, request.IbanLast4, request.AccountHolder, request.MandateConsentAt ?? request.RequestedAt);
        }

        request.Status = MemberRequestStatus.Approved;
        request.DecidedAt = clock.UtcNow.UtcDateTime;
        request.DecidedBy = actor.UserId;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.change-approved", "Member", member.Id.ToString(), null,
            JsonSerializer.Serialize(new { fields = ChangedFields(request) }, JsonSerializerOptions.Web)), cancellationToken);
        if ((request.Email ?? member.Email) is { Length: > 0 } address)
        {
            await email.SendAsync(Mail(address, member.FirstName ?? member.FullName, "Je gegevens zijn bijgewerkt",
                "De ledenadministratie heeft je wijziging verwerkt. Je ziet de nieuwe gegevens in de app onder Mijn gegevens."), cancellationToken);
        }
    }

    public async Task RejectChangeAsync(Guid requestId, string reason, CancellationToken cancellationToken)
    {
        var request = await db.MemberChangeRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.Status == MemberRequestStatus.Pending, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Verzoek niet gevonden of al afgehandeld.", DomainErrorKind.NotFound);
        request.Status = MemberRequestStatus.Rejected;
        request.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        request.DecidedAt = clock.UtcNow.UtcDateTime;
        request.DecidedBy = actor.UserId;
        // Een afgewezen IBAN bewaren we niet.
        (request.IbanProtected, request.AccountHolder) = (null, null);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.change-rejected", "Member", request.MemberId.ToString(), null,
            JsonSerializer.Serialize(new { reason = request.RejectionReason }, JsonSerializerOptions.Web)), cancellationToken);
    }

    /// <summary>Verbreken doorvoeren: beiden worden "lid" (eigen tarief); het tweede lid krijgt zijn eigen IBAN en machtiging.</summary>
    public async Task ApproveBreakAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await db.CombinationBreakRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.Status == CombinationBreakStatus.AwaitingApproval, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Verzoek niet gevonden, nog niet door beiden akkoord, of al afgehandeld.", DomainErrorKind.NotFound);
        var payer = await db.Members.SingleAsync(m => m.Id == request.PayerMemberId, cancellationToken);
        var partner = await db.Members.SingleAsync(m => m.Id == request.PartnerMemberId, cancellationToken);
        payer.MembershipKind = MembershipKind.OnePerson;
        partner.MembershipKind = MembershipKind.OnePerson;
        partner.PayerMemberId = null;
        SetBank(partner, request.PartnerIbanProtected!, request.PartnerIbanLast4, request.PartnerAccountHolder, request.PartnerMandateConsentAt ?? clock.UtcNow.UtcDateTime);
        request.Status = CombinationBreakStatus.Approved;
        request.DecidedAt = clock.UtcNow.UtcDateTime;
        request.DecidedBy = actor.UserId;
        (request.PartnerIbanProtected, request.PartnerAccountHolder) = (null, null);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.combination-broken", "Member", payer.Id.ToString(), null,
            JsonSerializer.Serialize(new { partner = partner.Id }, JsonSerializerOptions.Web)), cancellationToken);
        foreach (var m in new[] { payer, partner }.Where(x => x.Email is { Length: > 0 }))
        {
            await email.SendAsync(Mail(m.Email!, m.FirstName ?? m.FullName, "Jullie combinatielidmaatschap is verbroken",
                "De ledenadministratie heeft het verzoek goedgekeurd. Vanaf nu heeft ieder een eigen lidmaatschap en betaalt ieder de contributie voor één lid. Je jaren lid blijven gelijk."),
                cancellationToken);
        }
    }

    public async Task RejectBreakAsync(Guid requestId, string reason, CancellationToken cancellationToken)
    {
        var request = await db.CombinationBreakRequests.SingleOrDefaultAsync(
                r => r.Id == requestId && (r.Status == CombinationBreakStatus.AwaitingApproval || r.Status == CombinationBreakStatus.AwaitingAgreement), cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Verzoek niet gevonden of al afgehandeld.", DomainErrorKind.NotFound);
        request.Status = CombinationBreakStatus.Rejected;
        request.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        request.DecidedAt = clock.UtcNow.UtcDateTime;
        request.DecidedBy = actor.UserId;
        (request.PartnerIbanProtected, request.PartnerAccountHolder) = (null, null);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.combination-break-rejected", "Member", request.PayerMemberId.ToString(), null,
            JsonSerializer.Serialize(new { reason = request.RejectionReason }, JsonSerializerOptions.Web)), cancellationToken);
    }

    // ----- Hulpfuncties ---------------------------------------------------------------------------------------------

    /// <summary>Betaler en partner van de combinatie waar dit lid bij hoort, of (null, null).</summary>
    private async Task<(Member? Payer, Member? Partner)> PairAsync(Member me, CancellationToken cancellationToken)
    {
        if (me.MembershipKind == MembershipKind.Partner && me.PayerMemberId is { } payerId)
        {
            return (await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == payerId, cancellationToken), me);
        }

        var partner = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.PayerMemberId == me.Id, cancellationToken);
        return partner is null ? (null, null) : (me, partner);
    }

    private Task<CombinationBreakRequest?> OpenBreakAsync(Guid payerId, CancellationToken cancellationToken) =>
        db.CombinationBreakRequests.AsNoTracking().SingleOrDefaultAsync(
            r => r.PayerMemberId == payerId && (r.Status == CombinationBreakStatus.AwaitingAgreement || r.Status == CombinationBreakStatus.AwaitingApproval),
            cancellationToken);

    private Task<CombinationBreakRequest?> OpenBreakTrackedAsync(Guid payerId, CancellationToken cancellationToken) =>
        db.CombinationBreakRequests.SingleOrDefaultAsync(
            r => r.PayerMemberId == payerId && (r.Status == CombinationBreakStatus.AwaitingAgreement || r.Status == CombinationBreakStatus.AwaitingApproval),
            cancellationToken);

    private void Agree(CombinationBreakRequest request, bool asPayer, BankInput bank, DateTime now)
    {
        if (asPayer)
        {
            request.PayerAgreedAt = now;
        }
        else
        {
            // Het tweede lid gaat zelf betalen: IBAN en machtiging zijn verplicht.
            var b = Bank(bank.Iban, bank.AccountHolder, bank.MandateConsent);
            (request.PartnerIbanProtected, request.PartnerIbanLast4, request.PartnerAccountHolder, request.PartnerMandateConsentAt) = (b.Protected, b.Last4, b.Holder, now);
            request.PartnerAgreedAt = now;
        }

        if (request.PayerAgreedAt is not null && request.PartnerAgreedAt is not null)
        {
            request.Status = CombinationBreakStatus.AwaitingApproval;
        }
    }

    private (string Protected, string Last4, string Holder) Bank(string? iban, string? holder, bool mandateConsent)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            throw new DomainException(ErrorCodes.Validation, "Vul je IBAN in: je gaat de contributie zelf betalen.");
        }

        var normalized = MembershipApplications.NormalizeIban(iban);
        if (string.IsNullOrWhiteSpace(holder))
        {
            throw new DomainException(ErrorCodes.Validation, "Vul de naam van de rekeninghouder in.");
        }

        if (!mandateConsent)
        {
            throw new DomainException(ErrorCodes.Validation, "Geef toestemming voor de automatische incasso van de contributie.");
        }

        return (ibans.Protect(normalized), normalized[^4..], holder.Trim().Length > 100 ? holder.Trim()[..100] : holder.Trim());
    }

    private static void SetBank(Member member, string ibanProtected, string? last4, string? holder, DateTime consentAt)
    {
        member.IbanProtected = ibanProtected;
        member.IbanLast4 = last4;
        member.AccountHolder = holder;
        member.MandateReference = $"DVD-{member.MemberNumber}-{consentAt:yyyyMMdd}";
        member.MandateSignedOn = DateOnly.FromDateTime(consentAt);
    }

    private static List<string> ChangedFields(MemberChangeRequest r)
    {
        var fields = new List<string>();
        if (r.AddressLine is not null || r.PostalCode is not null || r.City is not null)
        {
            fields.Add("Adres");
        }

        if (r.Email is not null)
        {
            fields.Add("E-mailadres");
        }

        if (r.Phone is not null || r.MobilePhone is not null)
        {
            fields.Add("Telefoon");
        }

        if (r.IbanProtected is not null || r.IbanLast4 is not null)
        {
            fields.Add("IBAN");
        }

        return fields;
    }

    private static EmailMessage Mail(string to, string firstName, string subject, string body) =>
        new(to, subject, $"Beste {firstName},\n\n{body}\n\nMet vriendelijke groet,\nDe ledenadministratie van CV De Vrolijke Drammers",
            $"<p>Beste {WebUtility.HtmlEncode(firstName)},</p><p>{WebUtility.HtmlEncode(body)}</p><p>Met vriendelijke groet,<br>De ledenadministratie van CV De Vrolijke Drammers</p>",
            ReplyTo);
}
