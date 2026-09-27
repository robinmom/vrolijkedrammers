using System.Globalization;
using System.Net.Mail;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.EBoekhouden;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Provisioning;
using Drammers.Modules.Membership.Applications;
using Drammers.Modules.Membership.Guardians;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

/// <summary>Invoer van het formulier "Lid worden" (app, webpagina).</summary>
public sealed record ApplicationInput(
    string FirstName,
    string? NamePrefix,
    string LastName,
    string? Gender,
    DateOnly BirthDate,
    string AddressLine,
    string PostalCode,
    string City,
    string Email,
    string? Phone,
    string? GuardianName,
    string? GuardianPhone,
    string Iban,
    string AccountHolder,
    bool MandateConsent,
    bool PrivacyConsent,
    bool PhotoConsent,
    ApplicationSource Source);

/// <summary>
/// Lid worden (fase 9b, ADR-014 §1): formulier → e-mailcode (= indienen) → beoordeling door het bestuur → na goedkeuring
/// een idempotente saga: lid in e-Boekhouden (in Dev gesimuleerd) → lokaal lid → account (lid vanaf 16, anders de
/// ouder/verzorger met rol Ouder) → welkomstmail → bankgegevens gewist. Nooit automatisch een definitief lidmaatschap.
/// </summary>
public sealed class MembershipApplications(
    DrammersDbContext db,
    IAuditLogger audit,
    IOutbox outbox,
    IEmailSender email,
    IEBoekhoudenWriter ebWriter,
    MemberAccounts accounts,
    MemberSyncSettings syncSettings,
    ICurrentActor actor,
    IClock clock)
{
    public const string ProvisionMessageType = "membership.provision";

    /// <summary>Geldigheid van de e-mailcode en het maximale aantal pogingen.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(30);

    public const int MaxCodeAttempts = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record ProvisionMessage(Guid ApplicationId);

    // ----- Aanvrager ----------------------------------------------------------------------------------------------

    /// <summary>Legt de aanmelding vast als concept en stuurt een code naar het e-mailadres; geeft het (geheime) ID.</summary>
    public async Task<Guid> StartAsync(ApplicationInput input, string? ipAddress, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var application = Validate(input, today);
        application.Id = IdGenerator.NewId();
        application.MandateReference = $"DVD-{application.Id:N}"[..24].ToUpperInvariant();
        application.CreatedAt = clock.UtcNow.UtcDateTime;
        application.ConsentPrivacyAt = application.CreatedAt;
        application.MandateConsentAt = application.CreatedAt;
        application.IpHash = ipAddress is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ipAddress)));
        var code = NewCode(application);
        db.MembershipApplications.Add(application);
        await db.SaveChangesAsync(cancellationToken);
        await email.SendAsync(CodeMail(application.Email, application.IsMinorOn(today) ? application.GuardianName! : application.FirstName, code), cancellationToken);
        return application.Id;
    }

    public async Task ResendCodeAsync(Guid id, CancellationToken cancellationToken)
    {
        var application = await FindDraftAsync(id, cancellationToken);
        var code = NewCode(application);
        await db.SaveChangesAsync(cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        await email.SendAsync(CodeMail(application.Email, application.IsMinorOn(today) ? application.GuardianName! : application.FirstName, code), cancellationToken);
    }

    /// <summary>Bevestigt het e-mailadres met de code; daarmee is de aanmelding ingediend (wachtrij van het bestuur).</summary>
    public async Task VerifyAsync(Guid id, string code, CancellationToken cancellationToken)
    {
        var application = await FindDraftAsync(id, cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        if (application.VerificationAttempts >= MaxCodeAttempts || application.VerificationExpiresAt is not { } expires || expires < now)
        {
            throw new DomainException(ErrorCodes.VerificationCodeExpired, "De code is verlopen. Vraag een nieuwe code aan.", DomainErrorKind.Conflict);
        }

        application.VerificationAttempts++;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashCode(application.Id, code.Trim())), Encoding.ASCII.GetBytes(application.VerificationCodeHash ?? string.Empty)))
        {
            await db.SaveChangesAsync(cancellationToken);
            throw new DomainException(ErrorCodes.VerificationCodeInvalid, "Deze code klopt niet.", DomainErrorKind.Validation);
        }

        application.Status = ApplicationStatus.Submitted;
        application.EmailVerifiedAt = now;
        application.SubmittedAt = now;
        application.VerificationCodeHash = null;
        application.VerificationExpiresAt = null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("membership-application.submitted", "MembershipApplication", id.ToString(), null,
            JsonSerializer.Serialize(new { source = application.Source.ToString() }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ----- Bestuur ------------------------------------------------------------------------------------------------

    public async Task StartReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var application = await FindAsync(id, cancellationToken);
        if (application.Status != ApplicationStatus.Submitted)
        {
            throw Decided();
        }

        application.Status = ApplicationStatus.InReview;
        application.HandledBy = actor.UserId;
        application.HandledAt = clock.UtcNow.UtcDateTime;
        await SaveWithAuditAsync("membership-application.in-review", application, null, cancellationToken);
    }

    /// <summary>Goedkeuren (<c>member.approve</c>): start de provisioning. Alleen vanuit Ingediend of In behandeling.</summary>
    public async Task ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var application = await FindAsync(id, cancellationToken);
        if (application.Status is not (ApplicationStatus.Submitted or ApplicationStatus.InReview))
        {
            throw Decided();
        }

        // Eén account hoort bij hooguit één lid: vóór het aanmaken in e-Boekhouden controleren, niet halverwege de saga.
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        if (!application.IsMinorOn(today) && await EmailInUseAsync(application.Email, cancellationToken) is { } other)
        {
            throw new DomainException(ErrorCodes.MemberHasAccount,
                $"Het e-mailadres {application.Email} hoort al bij het app-account van {other}. Vraag de aanvrager om een eigen e-mailadres (of wijs de aanmelding af).",
                DomainErrorKind.Conflict);
        }

        var now = clock.UtcNow.UtcDateTime;
        application.Status = ApplicationStatus.Approved;
        application.HandledBy ??= actor.UserId;
        application.HandledAt ??= now;
        application.DecisionAt = now;
        outbox.Enqueue(ProvisionMessageType, new ProvisionMessage(id));
        await SaveWithAuditAsync("membership-application.approved", application, null, cancellationToken);
    }

    /// <summary>Afwijzen met reden; de bankgegevens worden direct gewist.</summary>
    public async Task RejectAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var application = await FindAsync(id, cancellationToken);
        if (application.Status is not (ApplicationStatus.Submitted or ApplicationStatus.InReview))
        {
            throw Decided();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(ErrorCodes.Validation, "Geef een reden voor het afwijzen.");
        }

        var now = clock.UtcNow.UtcDateTime;
        application.Status = ApplicationStatus.Rejected;
        application.RejectionReason = reason.Trim();
        application.HandledBy ??= actor.UserId;
        application.HandledAt ??= now;
        application.DecisionAt = now;
        WipeBankDetails(application);
        await SaveWithAuditAsync("membership-application.rejected", application, JsonSerializer.Serialize(new { reason = application.RejectionReason }, Json), cancellationToken);
    }

    public async Task SetNotesAsync(Guid id, string? notes, CancellationToken cancellationToken)
    {
        var application = await FindAsync(id, cancellationToken);
        application.InternalNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Opnieuw proberen na een mislukte provisioning.</summary>
    public async Task RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var application = await FindAsync(id, cancellationToken);
        if (application.Status is not (ApplicationStatus.ProvisioningFailed or ApplicationStatus.Provisioning or ApplicationStatus.Approved))
        {
            throw Decided();
        }

        outbox.Enqueue(ProvisionMessageType, new ProvisionMessage(id));
        await SaveWithAuditAsync("membership-application.retried", application, null, cancellationToken);
    }

    /// <summary>
    /// Naam en lidnummer van het lid wiens app-account dit e-mailadres al gebruikt, of <c>null</c>. Een ouderaccount zonder
    /// eigen lidmaatschap telt niet: dat account kan de rol Lid er zonder probleem bij krijgen.
    /// </summary>
    public async Task<string?> EmailInUseAsync(string email, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Email == email && u.AccountStatus != Modules.Identity.Users.AccountStatus.Deleted && u.MemberId != null)
            .Join(db.Members, u => u.MemberId, m => m.Id, (u, m) => m.FullName + " (lidnummer " + m.MemberNumber + ")")
            .FirstOrDefaultAsync(cancellationToken);

    // ----- Saga (worker) ------------------------------------------------------------------------------------------

    /// <summary>
    /// Idempotent: elke stap legt zijn resultaat vast (lidnummer, lokaal lid, account), zodat hervatten geen tweede lid in
    /// e-Boekhouden, tweede lokaal lid of tweede account oplevert.
    /// </summary>
    public async Task RunProvisioningAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await FindAsync(applicationId, cancellationToken);
        if (application.Status is ApplicationStatus.Activated)
        {
            return;
        }

        if (application.Status is not (ApplicationStatus.Approved or ApplicationStatus.Provisioning or ApplicationStatus.ProvisioningFailed))
        {
            throw Decided();
        }

        var saga = await db.AccountProvisioning.SingleOrDefaultAsync(
            p => p.SourceType == ProvisioningSourceType.MembershipApplication && p.SourceId == applicationId.ToString(), cancellationToken);
        if (saga is null)
        {
            saga = new AccountProvisioning
            {
                Id = IdGenerator.NewId(),
                SourceType = ProvisioningSourceType.MembershipApplication,
                SourceId = applicationId.ToString(),
                Kind = ProvisioningKind.Member,
                Step = ProvisioningStep.Pending,
                CreatedAt = clock.UtcNow.UtcDateTime,
            };
            db.AccountProvisioning.Add(saga);
        }

        application.Status = ApplicationStatus.Provisioning;
        application.ProvisioningId = saga.Id;
        saga.Attempts++;
        await db.SaveChangesAsync(cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var minor = application.IsMinorOn(today);
        try
        {
            if (saga.MemberNumber is null)
            {
                var created = await ebWriter.CreateOrFindMemberAsync(await ToEbMemberAsync(application, cancellationToken), cancellationToken);
                saga.MemberNumber = created.MemberNumber;
                saga.EbMemberId = created.EbMemberId?.ToString(CultureInfo.InvariantCulture);
                saga.Step = ProvisioningStep.EbCreated;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (saga.MemberId is null)
            {
                saga.MemberId = await CreateLocalMemberAsync(application, saga, today, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            if (saga.UserId is null)
            {
                saga.UserId = minor
                    ? await accounts.EnsureAccountAsync(application.Email, application.GuardianName!, null, DefaultRoles.Ouder, cancellationToken)
                    : await accounts.EnsureAccountAsync(application.Email, application.FullName, saga.MemberId, DefaultRoles.Lid, cancellationToken);
                if (minor)
                {
                    await EnsureGuardianRelationAsync(application, saga.MemberId.Value, saga.UserId.Value, cancellationToken);
                }

                saga.Step = ProvisioningStep.MemberCreated;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (saga.Step is not (ProvisioningStep.WelcomeSent or ProvisioningStep.Completed))
            {
                await email.SendAsync(
                    minor
                        ? GuardianWelcomeMail(application.Email, application.GuardianName!, application.FirstName)
                        : MemberAccounts.WelcomeMail(application.Email, application.FirstName),
                    cancellationToken);
                saga.Step = ProvisioningStep.WelcomeSent;
                await db.SaveChangesAsync(cancellationToken);
            }

            var now = clock.UtcNow.UtcDateTime;
            saga.Step = ProvisioningStep.Completed;
            saga.CompletedAt = now;
            saga.LastError = null;
            application.Status = ApplicationStatus.Activated;
            application.ResultingMemberId = saga.MemberId;
            WipeBankDetails(application);
            await SaveWithAuditAsync("membership-application.activated", application,
                JsonSerializer.Serialize(new { memberId = saga.MemberId, memberNumber = saga.MemberNumber, guardianAccount = minor }, Json), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            var error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            await db.AccountProvisioning.Where(p => p.Id == saga.Id).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.LastError, error).SetProperty(p => p.Attempts, saga.Attempts), cancellationToken);
            await db.MembershipApplications.Where(a => a.Id == applicationId).ExecuteUpdateAsync(
                s => s.SetProperty(a => a.Status, ApplicationStatus.ProvisioningFailed), cancellationToken);
            throw;
        }
    }

    private async Task<EbNewMember> ToEbMemberAsync(MembershipApplication application, CancellationToken cancellationToken)
    {
        var mapping = await syncSettings.GetMappingAsync(cancellationToken);
        var freeTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mapping.BirthDate is { } birthField)
        {
            freeTexts[birthField] = application.BirthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (mapping.JoinYear is { } joinField)
        {
            freeTexts[joinField] = clock.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        }

        var note = new StringBuilder($"Aangemeld via {SourceLabel(application.Source)} op {application.SubmittedAt:dd-MM-yyyy}.");
        note.Append(CultureInfo.InvariantCulture, $" Rekeninghouder: {application.AccountHolder}.");
        if (application.GuardianName is not null)
        {
            note.Append(CultureInfo.InvariantCulture, $" Ouder/verzorger: {application.GuardianName}, {application.GuardianPhone}.");
        }

        return new EbNewMember(
            application.FullName, application.Gender, application.AddressLine, application.PostalCode, application.City, "NL",
            application.GuardianPhone ?? application.Phone, application.Email, application.Iban,
            Mandate: application.Iban is not null, MandateType: application.Iban is null ? null : "D", application.MandateReference,
            application.MandateConsentAt is { } signed ? DateOnly.FromDateTime(signed) : null,
            note.ToString(), freeTexts);
    }

    private async Task<Guid> CreateLocalMemberAsync(MembershipApplication application, AccountProvisioning saga, DateOnly today, CancellationToken cancellationToken)
    {
        var number = saga.MemberNumber!;
        var existing = await db.Members.SingleOrDefaultAsync(m => m.MemberNumber == number, cancellationToken);
        if (existing is not null)
        {
            // De ledensync was ons voor: dat lid gebruiken.
            return existing.Id;
        }

        var member = new Member
        {
            Id = IdGenerator.NewId(),
            MemberNumber = number,
            EbMemberId = saga.EbMemberId is { } ebId ? int.Parse(ebId, CultureInfo.InvariantCulture) : null,
            FullName = application.FullName,
            FirstName = application.FirstName,
            NamePrefix = application.NamePrefix,
            LastName = application.LastName,
            Gender = application.Gender,
            AddressLine = application.AddressLine,
            PostalCode = application.PostalCode,
            City = application.City,
            Country = "NL",
            Email = application.Email,
            MobilePhone = application.GuardianPhone ?? application.Phone,
            BirthDate = application.BirthDate,
            JoinYear = (short)today.Year,
            MembershipStatus = MembershipStatus.Active,
            SyncState = MemberSyncState.InSync,
            EbLastSeenAt = clock.UtcNow.UtcDateTime,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return member.Id;
    }

    private async Task EnsureGuardianRelationAsync(MembershipApplication application, Guid memberId, Guid guardianUserId, CancellationToken cancellationToken)
    {
        if (await db.GuardianRelations.AnyAsync(g => g.MemberId == memberId && g.GuardianUserId == guardianUserId, cancellationToken))
        {
            return;
        }

        var now = clock.UtcNow.UtcDateTime;
        db.GuardianRelations.Add(new GuardianRelation
        {
            Id = IdGenerator.NewId(),
            MemberId = memberId,
            GuardianUserId = guardianUserId,
            GuardianName = application.GuardianName!,
            GuardianPhone = application.GuardianPhone,
            VerifiedAt = application.EmailVerifiedAt ?? now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    // ----- Hulpfuncties -------------------------------------------------------------------------------------------

    private static MembershipApplication Validate(ApplicationInput input, DateOnly today)
    {
        static string Required(string? value, string label, int max) =>
            string.IsNullOrWhiteSpace(value) ? throw Invalid($"Vul {label} in.")
            : value.Trim().Length > max ? throw Invalid($"{char.ToUpper(label[0], CultureInfo.InvariantCulture)}{label[1..]} is te lang.")
            : value.Trim();

        var application = new MembershipApplication
        {
            FirstName = Required(input.FirstName, "je voornaam", 50),
            NamePrefix = string.IsNullOrWhiteSpace(input.NamePrefix) ? null : input.NamePrefix.Trim(),
            LastName = Required(input.LastName, "je achternaam", 60),
            Gender = input.Gender is "m" or "v" ? input.Gender : null,
            BirthDate = input.BirthDate,
            AddressLine = Required(input.AddressLine, "straat en huisnummer", 150),
            PostalCode = Required(input.PostalCode, "de postcode", 10).ToUpperInvariant(),
            City = Required(input.City, "de woonplaats", 50),
            Email = Required(input.Email, "het e-mailadres", 150).ToLowerInvariant(),
            Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            Iban = NormalizeIban(input.Iban),
            AccountHolder = Required(input.AccountHolder, "de naam van de rekeninghouder", 100),
            ConsentPhoto = input.PhotoConsent,
            Source = input.Source,
            Status = ApplicationStatus.Draft,
            MandateReference = string.Empty,
        };

        if (!MailAddress.TryCreate(application.Email, out _))
        {
            throw Invalid("Het e-mailadres is ongeldig.");
        }

        var age = application.AgeOn(today);
        if (input.BirthDate > today || age > 120)
        {
            throw Invalid("De geboortedatum is ongeldig.");
        }

        if (age < MembershipApplication.MinimumAgeMembership)
        {
            throw Invalid($"Lid worden kan vanaf {MembershipApplication.MinimumAgeMembership} jaar.");
        }

        if (application.IsMinorOn(today))
        {
            application.GuardianName = Required(input.GuardianName, "de naam van de ouder of verzorger", 100);
            application.GuardianPhone = Required(input.GuardianPhone, "het telefoonnummer van de ouder of verzorger", 30);
            application.GuardianEmail = application.Email;
        }

        if (!input.PrivacyConsent)
        {
            throw Invalid("Ga akkoord met de privacyverklaring.");
        }

        if (!input.MandateConsent)
        {
            throw Invalid("Geef toestemming voor de automatische incasso van de contributie.");
        }

        return application;
    }

    /// <summary>IBAN zonder spaties en in hoofdletters, gecontroleerd met de mod-97-controle.</summary>
    public static string NormalizeIban(string? value)
    {
        var iban = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (iban.Length is < 15 or > 34 || !char.IsLetter(iban[0]) || !char.IsLetter(iban[1]))
        {
            throw Invalid("Het IBAN-nummer is ongeldig.");
        }

        var rearranged = iban[4..] + iban[..4];
        var digits = string.Concat(rearranged.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString()));
        return BigInteger.Parse(digits, CultureInfo.InvariantCulture) % 97 == 1 ? iban : throw Invalid("Het IBAN-nummer is ongeldig.");
    }

    /// <summary>Voor het portal: alleen landcode en de laatste vier cijfers.</summary>
    public static string? MaskIban(string? iban) => iban is null ? null : $"{iban[..2]}** **** **** {iban[^4..]}";

    private static void WipeBankDetails(MembershipApplication application)
    {
        application.Iban = null;
        application.AccountHolder = null;
    }

    private string NewCode(MembershipApplication application)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        application.VerificationCodeHash = HashCode(application.Id, code);
        application.VerificationExpiresAt = clock.UtcNow.UtcDateTime + CodeLifetime;
        application.VerificationAttempts = 0;
        return code;
    }

    private static string HashCode(Guid id, string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:N}:{code}")));

    private async Task<MembershipApplication> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.MembershipApplications.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
        ?? throw new DomainException(ErrorCodes.ApplicationNotFound, "Aanmelding niet gevonden.", DomainErrorKind.NotFound);

    private async Task<MembershipApplication> FindDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        var application = await db.MembershipApplications.SingleOrDefaultAsync(a => a.Id == id && a.Status == ApplicationStatus.Draft, cancellationToken);
        return application ?? throw new DomainException(ErrorCodes.ApplicationNotFound, "Deze aanmelding is al bevestigd of bestaat niet.", DomainErrorKind.NotFound);
    }

    private async Task SaveWithAuditAsync(string action, MembershipApplication application, string? values, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry(action, "MembershipApplication", application.Id.ToString(), null, values), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static DomainException Invalid(string message) => new(ErrorCodes.Validation, message);

    private static DomainException Decided() =>
        new(ErrorCodes.ApplicationDecided, "Deze aanmelding is al afgehandeld of heeft een andere status.", DomainErrorKind.Conflict);

    private static string SourceLabel(ApplicationSource source) => source switch
    {
        ApplicationSource.App => "de app",
        ApplicationSource.Website => "de website",
        _ => "het beheerportal",
    };

    // ----- E-mails (concepten; het bestuur keurt de teksten goed) --------------------------------------------------

    public static EmailMessage CodeMail(string to, string name, string code)
    {
        const string Subject = "Je code om je aanmelding te bevestigen";
        var text = $"""
            Beste {name},

            Bedankt voor je aanmelding bij De Vrolijke Drammers! Vul deze code in om je e-mailadres te bevestigen:

            {code}

            De code is 30 minuten geldig. Daarna beoordeelt het bestuur je aanmelding; je hoort daarna van ons.

            Heb je je niet aangemeld? Dan kun je deze e-mail negeren.

            Groeten,
            De Vrolijke Drammers
            """;
        var html = $"""
            <p>Beste {System.Net.WebUtility.HtmlEncode(name)},</p>
            <p>Bedankt voor je aanmelding bij De Vrolijke Drammers! Vul deze code in om je e-mailadres te bevestigen:</p>
            <p style="font-size:24px;font-weight:bold;letter-spacing:4px">{code}</p>
            <p>De code is 30 minuten geldig. Daarna beoordeelt het bestuur je aanmelding; je hoort daarna van ons.</p>
            <p>Heb je je niet aangemeld? Dan kun je deze e-mail negeren.</p>
            <p>Groeten,<br>De Vrolijke Drammers</p>
            """;
        return new EmailMessage(to, Subject, text, html);
    }

    public static EmailMessage GuardianWelcomeMail(string to, string guardianName, string childFirstName)
    {
        const string Subject = "Welkom bij De Vrolijke Drammers";
        var text = $"""
            Beste {guardianName},

            {childFirstName} is lid van De Vrolijke Drammers. Welkom!

            Als ouder/verzorger kun je de app van de vereniging gebruiken. Zo log je de eerste keer in:
            1. Open de app en kies Meer → Inloggen.
            2. Kies op de inlogpagina voor een nieuw account ("Maak er een") en vul dit e-mailadres in: {to}.
            3. Je krijgt een code per e-mail. Vul die in; een wachtwoord is niet nodig.

            De contributie wordt automatisch geïncasseerd volgens de machtiging die je hebt gegeven.

            Groeten,
            De Vrolijke Drammers
            """;
        var html = $"""
            <p>Beste {System.Net.WebUtility.HtmlEncode(guardianName)},</p>
            <p>{System.Net.WebUtility.HtmlEncode(childFirstName)} is lid van De Vrolijke Drammers. Welkom!</p>
            <p>Als ouder/verzorger kun je de app van de vereniging gebruiken. <strong>Zo log je de eerste keer in:</strong></p>
            <ol><li>Open de app en kies <em>Meer → Inloggen</em>.</li><li>Kies op de inlogpagina voor een nieuw account (&quot;Maak er een&quot;) en vul dit e-mailadres in: {System.Net.WebUtility.HtmlEncode(to)}.</li><li>Je krijgt een code per e-mail. Vul die in; een wachtwoord is niet nodig.</li></ol>
            <p>De contributie wordt automatisch geïncasseerd volgens de machtiging die je hebt gegeven.</p>
            <p>Groeten,<br>De Vrolijke Drammers</p>
            """;
        return new EmailMessage(to, Subject, text, html);
    }
}

/// <summary>Voert de provisioning van een goedgekeurde aanmelding uit (worker).</summary>
public sealed class MembershipProvisioningHandler(MembershipApplications applications) : IOutboxMessageHandler
{
    public string Type => MembershipApplications.ProvisionMessageType;

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken) =>
        applications.RunProvisioningAsync(
            JsonSerializer.Deserialize<MembershipApplications.ProvisionMessage>(message.Payload, JsonSerializerOptions.Web)!.ApplicationId, cancellationToken);
}
