using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

/// <summary><paramref name="YouthRegistrationNumber"/> is gevuld als er ook jeugd is ingeschreven (wens 2026-11-21).</summary>
public sealed record PublicSubmitted(int RegistrationNumber, string StatusToken, int? YouthRegistrationNumber);

public sealed record PublicStatus(
    string ParadeName, string? GroupName, int? RegistrationNumber, RegistrationStatus Status, int? StartNumber, string? CategoryName, string? Reason);

/// <summary>
/// Inschrijving zonder account (fase 11c, B-05a): gasten in de app en het webformulier op de computer. Alles wordt in één
/// keer ingestuurd en volledig gecontroleerd; pas na de e-mailcode volgen het opgavenummer en een statuslink (alleen
/// lezen). Niet-bevestigde inschrijvingen worden na 48 uur opgeruimd. De commissie beoordeelt daarna zoals bij leden.
/// </summary>
public sealed class ParadePublicRegistrations(
    DrammersDbContext db, ParadeAdministration parades, IEmailSender email, IAuditLogger audit, ParadeChangeContext changeContext,
    CarnivalGroups groups, IClock clock)
{
    /// <summary>
    /// Geldigheid van de e-mailcode. Een dag, geen half uur: op 13-11-2026 deed Microsoft er 25 tot 106 minuten over om
    /// een mail te bezorgen, waardoor bij "Lid worden" elke code al verlopen in de inbox lag. Brute kracht blijft dicht
    /// via <see cref="MaxAttempts"/>: zes cijfers en hoogstens vijf pogingen, hoe lang de code ook geldig is.
    /// </summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan UnverifiedLifetime = TimeSpan.FromHours(48);
    public const int MaxAttempts = 5;

    /// <summary>Hoe vaak een nieuwe code kan worden gestuurd; de pogingen tellen over alle codes samen.</summary>
    public const int MaxResends = 3;

    private DateTime Now => clock.UtcNow.UtcDateTime;

    public async Task<Guid> StartAsync(
        RegistrationInput input, YouthRegistrationInput? youth, bool rulesAccepted, string baseUrl, CancellationToken cancellationToken)
    {
        var parade = await parades.CurrentAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is nog geen optocht om voor in te schrijven.", DomainErrorKind.NotFound);
        if (!parade.IsRegistrationOpen(Now))
        {
            throw new DomainException(ErrorCodes.RegistrationClosed, Now < parade.RegistrationOpensAt ? "De inschrijving is nog niet geopend." : "De inschrijving voor de optocht is gesloten.", DomainErrorKind.Conflict);
        }

        if (!rulesAccepted)
        {
            throw new DomainException(ErrorCodes.Validation, "Bevestig dat de gegevens juist zijn en dat je akkoord gaat met het optochtreglement.");
        }

        var registration = new ParadeRegistration
        {
            Id = IdGenerator.NewId(),
            ParadeId = parade.Id,
            CarnivalYearId = parade.CarnivalYearId,
            Status = RegistrationStatus.Draft,
            Source = RegistrationSource.WebForm,
        };
        ParadeRegistrations.ApplyInput(registration, input);
        var category = (await parades.CategoriesForAsync(parade.Id, activeOnly: true, cancellationToken)).SingleOrDefault(c => c.Id == input.CategoryId);
        var issues = RegistrationRules.Validate(registration, category, parade.SubjectRequired, forSubmit: true);
        if (!string.IsNullOrWhiteSpace(input.ContactPhone) && registration.ContactPhone is null)
        {
            issues.Add(new ValidationIssue(RegistrationFields.ContactPhone, "Vul een geldig telefoonnummer in, bijvoorbeeld 06 12345678.", IssueSeverity.Block));
        }

        if (input.CategoryId is not null && category is null)
        {
            issues.Add(new ValidationIssue(RegistrationFields.Category, "Deze categorie bestaat niet (meer).", IssueSeverity.Block));
        }

        if (issues.Any(i => i.Severity == IssueSeverity.Block))
        {
            throw new DomainException(ErrorCodes.RegistrationInvalid, "Nog niet alles is goed ingevuld.")
            {
                Issues = [.. issues.Select(i => new FieldIssue(i.Field, i.Message, i.Severity.ToString()))],
            };
        }

        var code = NewCode(registration);
        changeContext.Source = RegistrationSource.WebForm;
        db.ParadeRegistrations.Add(registration);

        // Ook jeugd ingeschreven (wens 2026-11-21): een eigen inschrijving die alles overneemt behalve haar vier eigen
        // velden. Ze wordt nu al vastgelegd als concept en krijgt haar nummer bij dezelfde e-mailcode.
        if (youth is not null)
        {
            var youthRegistration = ParadeRegistrations.NewYouthFor(registration, youth);
            var youthCategory = (await parades.CategoriesForAsync(parade.Id, activeOnly: true, cancellationToken)).SingleOrDefault(c => c.Id == youth.CategoryId);
            var youthIssues = ParadeRegistrations.YouthIssues(youthRegistration, youthCategory, parade.SubjectRequired, forSubmit: true);
            if (youthIssues.Any(i => i.Severity == IssueSeverity.Block))
            {
                throw new DomainException(ErrorCodes.RegistrationInvalid, "De jeugdinschrijving is nog niet goed ingevuld.")
                {
                    Issues = [.. youthIssues.Select(i => new FieldIssue(i.Field, i.Message, i.Severity.ToString()))],
                };
            }

            db.ParadeRegistrations.Add(youthRegistration);
        }

        await db.SaveChangesAsync(cancellationToken);
        await email.SendAsync(CodeMail(registration.ContactEmail!, registration.ContactName!, code, registration.Id, baseUrl), cancellationToken);
        return registration.Id;
    }

    public async Task ResendCodeAsync(Guid id, string baseUrl, CancellationToken cancellationToken)
    {
        var registration = await PendingAsync(id, cancellationToken);
        // Een rem op mail naar een (mogelijk vreemd) adres, en geen nieuwe pogingen door steeds een nieuwe code te vragen.
        if (registration.VerificationResends >= MaxResends || registration.VerificationAttempts >= MaxAttempts)
        {
            throw new DomainException(ErrorCodes.VerificationResendLimit,
                "Er kan geen nieuwe code meer worden gestuurd. Controleer het e-mailadres (en de map Ongewenst) of schrijf opnieuw in.", DomainErrorKind.Conflict);
        }

        registration.VerificationResends++;
        var code = NewCode(registration);
        await db.SaveChangesAsync(cancellationToken);
        await email.SendAsync(CodeMail(registration.ContactEmail!, registration.ContactName!, code, registration.Id, baseUrl), cancellationToken);
    }

    /// <summary>
    /// De commissie stuurt de code opnieuw (wens 2026-11-13). De rem van <see cref="MaxResends"/> geldt hier niet: die
    /// zit er tegen mail naar een vreemd adres en tegen eindeloos nieuwe codes, en nu zit er een commissielid tussen.
    /// Het aantal pogingen gaat wel terug naar nul, anders is een inschrijving na vijf verkeerde codes niet meer te
    /// redden. Bedoeld voor wie zijn code nooit heeft gekregen doordat de mail onderweg bleef hangen.
    /// </summary>
    public async Task ResendCodeByCommissionAsync(Guid id, string baseUrl, CancellationToken cancellationToken)
    {
        var registration = await PendingAsync(id, cancellationToken);
        registration.VerificationResends++;
        registration.VerificationAttempts = 0;
        var code = NewCode(registration);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade-registration.code-resent", "ParadeRegistration", id.ToString(), null,
            JsonSerializer.Serialize(new { resends = registration.VerificationResends })), cancellationToken);
        await email.SendAsync(CodeMail(registration.ContactEmail!, registration.ContactName!, code, registration.Id, baseUrl), cancellationToken);
    }

    /// <summary>E-mailcode klopt: opgavenummer toekennen (ADR-011), statuslink maken en de bevestiging sturen.</summary>
    /// <param name="id">De inschrijving.</param>
    /// <param name="code">De code uit de e-mail.</param>
    /// <param name="baseUrl">Webadres van de site, voor de statuslink in de bevestiging.</param>
    /// <param name="cancellationToken">Annulering.</param>
    public async Task<PublicSubmitted> VerifyAsync(Guid id, string code, string baseUrl, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var registration = await PendingAsync(id, cancellationToken);
        if (registration.VerificationAttempts >= MaxAttempts)
        {
            throw new DomainException(ErrorCodes.VerificationCodeExpired, "Te vaak een verkeerde code ingevuld. Schrijf opnieuw in.", DomainErrorKind.Conflict);
        }

        if (registration.VerificationExpiresAt is not { } expires || expires < Now)
        {
            throw new DomainException(ErrorCodes.VerificationCodeExpired, "De code is verlopen. Vraag een nieuwe code aan.", DomainErrorKind.Conflict);
        }

        registration.VerificationAttempts++;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash($"{id:N}:{code.Trim()}")), Encoding.ASCII.GetBytes(registration.VerificationCodeHash ?? "")))
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new DomainException(ErrorCodes.VerificationCodeInvalid, "Deze code klopt niet.");
        }

        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == registration.ParadeId, cancellationToken);
        if (!parade.IsRegistrationOpen(Now))
        {
            throw new DomainException(ErrorCodes.RegistrationClosed, "De inschrijving voor de optocht is intussen gesloten.", DomainErrorKind.Conflict);
        }

        var number = await ParadeRegistrations.NextNumberAsync(db, parade.Id, cancellationToken);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        registration.RegistrationNumber = number;
        registration.Status = RegistrationStatus.Submitted;
        registration.SubmittedAt = Now;
        registration.ContactEmailVerifiedAt = Now;
        registration.VerificationCodeHash = null;
        registration.VerificationExpiresAt = null;
        registration.StatusTokenHash = Hash(token);
        db.ParadeStatusHistory.Add(new ParadeStatusHistory { RegistrationId = id, FromStatus = RegistrationStatus.Draft, ToStatus = RegistrationStatus.Submitted, OccurredAt = Now });
        changeContext.Source = RegistrationSource.WebForm;
        await groups.LinkOnSubmitAsync(registration, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade-registration.submitted", "ParadeRegistration", id.ToString(), null,
            JsonSerializer.Serialize(new { registrationNumber = number, source = "WebForm" })), cancellationToken);
        var youth = await db.ParadeRegistrations.SingleOrDefaultAsync(r => r.YouthOfRegistrationId == id, cancellationToken);
        string? youthToken = null;
        if (youth is { Status: RegistrationStatus.Draft })
        {
            youthToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            youth.RegistrationNumber = await ParadeRegistrations.NextNumberAsync(db, parade.Id, cancellationToken);
            youth.Status = RegistrationStatus.Submitted;
            youth.SubmittedAt = Now;
            youth.ContactEmailVerifiedAt = Now;
            youth.StatusTokenHash = Hash(youthToken);
            db.ParadeStatusHistory.Add(new ParadeStatusHistory
            {
                RegistrationId = youth.Id,
                FromStatus = RegistrationStatus.Draft,
                ToStatus = RegistrationStatus.Submitted,
                OccurredAt = Now,
            });
            await groups.LinkOnSubmitAsync(youth, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("parade-registration.submitted", "ParadeRegistration", youth.Id.ToString(), null,
                JsonSerializer.Serialize(new { registrationNumber = youth.RegistrationNumber, youthOf = id, source = "WebForm" })), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await email.SendAsync(ConfirmationMail(registration, parade.Name, token, baseUrl), cancellationToken);
        if (youth is not null && youthToken is not null)
        {
            // Een eigen bevestiging met een eigen statuslink: de jeugd is een eigen inschrijving.
            await email.SendAsync(ConfirmationMail(youth, parade.Name, youthToken, baseUrl), cancellationToken);
        }

        return new PublicSubmitted(number, token, youth?.RegistrationNumber);
    }

    /// <summary>Status via de statuslink: alleen de eigen inschrijving, zonder contactgegevens.</summary>
    public async Task<PublicStatus> StatusAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token ?? "");
        var r = await db.ParadeRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.StatusTokenHash == hash, cancellationToken)
            ?? throw new DomainException(ErrorCodes.RegistrationNotFound, "Deze statuslink is niet (meer) geldig.", DomainErrorKind.NotFound);
        var parade = await db.Parades.AsNoTracking().Where(p => p.Id == r.ParadeId).Select(p => p.Name).SingleAsync(cancellationToken);
        var category = await db.ParadeCategories.AsNoTracking().Where(c => c.Id == r.CategoryId).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken);
        var reason = r.Status is RegistrationStatus.Rejected or RegistrationStatus.AdditionalInformationRequired
            ? await db.ParadeStatusHistory.AsNoTracking().Where(h => h.RegistrationId == r.Id && h.ToStatus == r.Status).OrderByDescending(h => h.OccurredAt).Select(h => h.Reason).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new PublicStatus(parade, r.GroupName, r.RegistrationNumber, r.Status, r.Status is RegistrationStatus.StartNumberAssigned or RegistrationStatus.Final ? r.StartNumber : null, category, reason);
    }

    /// <summary>
    /// Opruimen: niet-bevestigde inschrijvingen van het webformulier ouder dan 48 uur (zonder nummer). Eerst de
    /// jeugdinschrijvingen: die verwijzen naar hun hoofdinschrijving, en die verwijzing staat op Restrict (een
    /// zelfverwijzende cascade staat SQL Server niet toe), dus in één delete zou de volgorde fout kunnen uitpakken.
    /// </summary>
    public async Task<int> PurgeUnverifiedAsync(CancellationToken cancellationToken)
    {
        var before = Now - UnverifiedLifetime;
        var stale = db.ParadeRegistrations
            .Where(r => r.Source == RegistrationSource.WebForm && r.Status == RegistrationStatus.Draft && r.CreatedAt < before);
        var youth = await stale.Where(r => r.YouthOfRegistrationId != null).ExecuteDeleteAsync(cancellationToken);
        return youth + await stale.Where(r => r.YouthOfRegistrationId == null).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<ParadeRegistration> PendingAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ParadeRegistrations.SingleOrDefaultAsync(r => r.Id == id && r.Source == RegistrationSource.WebForm && r.Status == RegistrationStatus.Draft, cancellationToken)
        ?? throw new DomainException(ErrorCodes.RegistrationNotFound, "Inschrijving niet gevonden of al bevestigd.", DomainErrorKind.NotFound);

    private string NewCode(ParadeRegistration registration)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        registration.VerificationCodeHash = Hash($"{registration.Id:N}:{code}");
        registration.VerificationExpiresAt = Now + CodeLifetime;
        return code;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// De code, met een link terug naar het formulier. Die link is nodig omdat het id van de inschrijving alleen in de
    /// geopende pagina (of het app-scherm) leeft: wie het tabblad sluit voordat de mail aankomt, kan de code nergens meer
    /// invullen. De code staat in de link zodat één klik genoeg is; bevestigen gebeurt pas na een druk op de knop.
    /// </summary>
    public static EmailMessage CodeMail(string to, string name, string code, Guid id, string baseUrl)
    {
        const string Subject = "Je code om de inschrijving voor de optocht te bevestigen";
        var link = $"{baseUrl.TrimEnd('/')}/optocht-inschrijven/?bevestigen={id}&code={code}";
        var text = $"Beste {name},\n\nVul deze code in om jullie inschrijving voor de optocht te bevestigen:\n\n{code}\n\nOf open deze link, dan staat de code al klaar: {link}\n\nDe code is 24 uur geldig. Pas na het bevestigen krijgen jullie een opgavenummer.\n\nHeb je niets ingevuld? Dan kun je deze e-mail negeren.\n\nGroeten,\nDe Vrolijke Drammers";
        var html = $"<p>Beste {WebUtility.HtmlEncode(name)},</p><p>Vul deze code in om jullie inschrijving voor de optocht te bevestigen:</p><p style=\"font-size:24px;font-weight:bold;letter-spacing:4px\">{code}</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Of bevestig de inschrijving met deze link</a>, dan staat de code al klaar.</p><p>De code is 24 uur geldig. Pas na het bevestigen krijgen jullie een opgavenummer.</p><p>Heb je niets ingevuld? Dan kun je deze e-mail negeren.</p><p>Groeten,<br>De Vrolijke Drammers</p>";
        return new EmailMessage(to, Subject, text, html);
    }

    public static EmailMessage ConfirmationMail(ParadeRegistration r, string paradeName, string token, string baseUrl)
    {
        var link = $"{baseUrl.TrimEnd('/')}/optocht-inschrijven/?status={token}";
        var subject = $"Inschrijving {paradeName}: opgavenummer {r.RegistrationNumber}";
        var text = $"Beste {r.ContactName},\n\nBedankt voor de inschrijving van {r.GroupName} voor de {paradeName}.\n\nJullie opgavenummer is {r.RegistrationNumber}. Dit is de volgorde van binnenkomst, niet jullie startnummer.\n\nDe optochtcommissie beoordeelt de inschrijving; pas na goedkeuring is ze definitief. Je hoort het per e-mail.\n\nDe status bekijk je via deze link (bewaar deze e-mail): {link}\n\nGroeten,\nDe Vrolijke Drammers";
        var html = $"<p>Beste {WebUtility.HtmlEncode(r.ContactName)},</p><p>Bedankt voor de inschrijving van <strong>{WebUtility.HtmlEncode(r.GroupName)}</strong> voor de {WebUtility.HtmlEncode(paradeName)}.</p><p style=\"font-size:20px\">Jullie opgavenummer is <strong>{r.RegistrationNumber}</strong>.</p><p>Dit is de volgorde van binnenkomst, <strong>niet</strong> jullie startnummer. De optochtcommissie beoordeelt de inschrijving; pas na goedkeuring is ze definitief. Je hoort het per e-mail.</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Bekijk de status van jullie inschrijving</a> (bewaar deze e-mail).</p><p>Groeten,<br>De Vrolijke Drammers</p>";
        return new EmailMessage(r.ContactEmail!, subject, text, html);
    }
}
