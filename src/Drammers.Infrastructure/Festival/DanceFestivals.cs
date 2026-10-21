using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Contact;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Exports;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Payments;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Festival;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Festival;

public enum FestivalState
{
    /// <summary>Er is geen actieve editie of de pagina staat uit.</summary>
    Hidden,
    NotYetOpen,
    Open,
    Closed,
}

public sealed record FestivalEntryInput(
    Guid? Id, DanceType DanceType, DanceFormat Format, string Names, decimal AverageAge, int DancerCount, string LeaderName, string? Title, ShowStyle? ShowStyle);

public sealed record FestivalRegistrationInput(
    string ClubName, string ContactName, string AddressLine, string PostalCode, string City, string Phone, string Email, string? Remarks,
    bool? PhotoConsent, bool PrivacyAccepted, bool RulesAccepted, IReadOnlyList<FestivalEntryInput> Entries);

public sealed record FestivalSettingsInput(
    string Name, DateOnly FestivalDate, string Location, bool PageEnabled, DateTime? RegistrationOpensAt, DateTime? RegistrationClosesAt, int Capacity,
    bool PaymentsEnabled, int PriceSoloDuoCents, int PriceGroupOneDanceCents, int PriceGroupTwoDancesCents, string NotificationEmail,
    int VatPercent = 21, bool ServiceFeeOn = false, int ServiceFeeCents = 0);

public enum FestivalDecision
{
    Approve,
    RequestInfo,
    Waitlist,
    Reject,
}

public sealed record FestivalPublicView(
    FestivalState State, string Name, DateOnly FestivalDate, string Location, DateTime? RegistrationOpensAt, DateTime? RegistrationClosesAt,
    int Capacity, int PlacesTaken, int PriceSoloDuoCents, int PriceGroupOneDanceCents, int PriceGroupTwoDancesCents, bool PaymentsEnabled,
    bool HasRules, bool HasPrivacy, DateOnly AgeReferenceDate);

public sealed record FestivalEntryView(
    Guid Id, int SortOrder, DanceType DanceType, DanceFormat Format, string Names, decimal AverageAge, int DancerCount, string LeaderName, string? Title,
    ShowStyle? ShowStyle, DanceCategory Category, DanceEntryStatus Status, bool HoldsPlace, int? WaitlistPosition, DateTime? RemovedAt);

/// <summary><see cref="DuplicatePaid"/>: zo vaak is dit bedrag extra betaald (terugbetalen).</summary>
public sealed record FestivalPaymentView(
    Guid Id, int AmountCents, DanceFestivalPaymentStatus Status, string? MollieStatus, DateTime CreatedAt, DateTime? PaidAt, int DuplicatePaid);

/// <summary>Wat de vereniging via de link in de mail ziet en kan wijzigen.</summary>
public sealed record FestivalOwnRegistrationView(
    Guid Id, int Number, string ClubName, string ContactName, string AddressLine, string PostalCode, string City, string Phone, string Email,
    string? Remarks, bool PhotoConsent, string? InfoRequest, bool Editable, IReadOnlyList<FestivalEntryView> Entries,
    IReadOnlyList<FestivalPaymentView> Payments);

public sealed record FestivalRegistrationSummary(
    Guid Id, int Number, string ClubName, string ContactName, int Dances, int Approved, int ToReview, int Waitlisted, int? FirstWaitlistPosition,
    string Status, string PaymentStatus, DateTime SubmittedAt, DateTime? ChangedAt);

public sealed record FestivalStats(int Capacity, int PlacesTaken, int ToReview, int Waitlisted, int PaidCents, int RequestedCents);

public sealed record FestivalAdminList(FestivalSettingsView? Festival, FestivalStats Stats, IReadOnlyList<FestivalRegistrationSummary> Registrations);

public sealed record FestivalVersionInfo(int Version, DateTime CreatedAt);

public sealed record FestivalRegistrationDetail(
    Guid Id, int Number, string ClubName, string ContactName, string AddressLine, string PostalCode, string City, string Phone, string Email,
    string? Remarks, bool PhotoConsent, DateTime PrivacyAcceptedAt, DateTime RulesAcceptedAt, DateTime SubmittedAt, DateTime? ChangedAt, int Version,
    string? InfoRequest, IReadOnlyList<FestivalEntryView> Entries, IReadOnlyList<FestivalVersionInfo> Versions, IReadOnlyList<FestivalPaymentView> Payments,
    int SuggestedAmountCents);

public sealed record FestivalSettingsView(
    Guid Id, string Name, DateOnly FestivalDate, string Location, bool PageEnabled, DateTime? RegistrationOpensAt, DateTime? RegistrationClosesAt,
    int Capacity, bool PaymentsEnabled, int PriceSoloDuoCents, int PriceGroupOneDanceCents, int PriceGroupTwoDancesCents, string NotificationEmail,
    bool HasRules, bool HasPrivacy, FestivalState State, int VatPercent, bool ServiceFeeOn, int ServiceFeeCents);

/// <summary>
/// Dansgardefestival (2026-10-12): openbaar inschrijven per vereniging met meerdere dansen, wijzigen via de link in de
/// bevestigingsmail, beoordelen per dans (goedkeuren, meer informatie, wachtlijst, afwijzen) met een vast aantal plekken
/// (één per goedgekeurde dans) en betaallinks via Mollie na goedkeuring.
/// </summary>
public sealed partial class DanceFestivals(
    DrammersDbContext db,
    IAuditLogger audit,
    IEmailSender email,
    IClock clock,
    IMollieClient mollie,
    IFileStore files,
    ContentFiles contentFiles,
    IOptions<TurnstileOptions> turnstile,
    ITurnstileVerifier verifier,
    IDataProtectionProvider protection,
    IOptions<EmailOptions> emailOptions,
    ILogger<DanceFestivals> logger)
{
    public const int MaxEntries = 40;

    /// <summary>Afzender (vaste eigen afzender); antwoorden gaan naar het meldingsadres van de editie.</summary>
    public const string SenderLocalPart = "secretaris";

    public const string DefaultNotificationEmail = "secretaris@vrolijkedrammers.nl";

    private static readonly TimeSpan MinimumFillTime = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _tokens = protection.CreateProtector("Drammers.DanceFestivalToken.v1");

    private DateTime Now => clock.UtcNow.UtcDateTime;

    // ---- Openbaar ----------------------------------------------------------------------------------------------------

    public async Task<FestivalPublicView?> PublicAsync(CancellationToken cancellationToken)
    {
        var festival = await ActiveAsync(cancellationToken);
        if (festival is null || !festival.PageEnabled)
        {
            return null;
        }

        return new FestivalPublicView(
            StateOf(festival), festival.Name, festival.FestivalDate, festival.Location, festival.RegistrationOpensAt, festival.RegistrationClosesAt,
            festival.Capacity, await PlacesTakenAsync(festival.Id, cancellationToken), festival.PriceSoloDuoCents, festival.PriceGroupOneDanceCents,
            festival.PriceGroupTwoDancesCents, festival.PaymentsEnabled, festival.RulesBlobPath is not null, festival.PrivacyBlobPath is not null,
            AgeReferenceDate(festival));
    }

    /// <summary>Het reglement of de privacyverklaring (PDF) van de actieve editie.</summary>
    public async Task<Stream?> OpenDocumentAsync(string kind, CancellationToken cancellationToken)
    {
        var festival = await ActiveAsync(cancellationToken);
        var path = kind switch { "reglement" => festival?.RulesBlobPath, "privacyverklaring" => festival?.PrivacyBlobPath, _ => null };
        return festival is { PageEnabled: true } && path is not null ? await files.OpenReadAsync(FileContainers.Content, path, cancellationToken) : null;
    }

    /// <summary>Nieuwe inschrijving; geeft het nummer, of <c>null</c> als het als spam is weggegooid.</summary>
    public async Task<int?> SubmitAsync(
        FestivalRegistrationInput input, string? website, int? elapsedMs, string? turnstileToken, string? remoteIp, string baseUrl,
        CancellationToken cancellationToken)
    {
        var festival = await ActiveAsync(cancellationToken);
        if (festival is null || StateOf(festival) != FestivalState.Open)
        {
            throw new DomainException(ErrorCodes.FestivalClosed, "Inschrijven voor het Dansgardefestival is nu niet mogelijk.", DomainErrorKind.Conflict);
        }

        if (turnstile.Value.Enabled && !await verifier.VerifyAsync(turnstileToken, remoteIp, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "De controle of je geen robot bent is mislukt. Probeer het opnieuw.");
        }

        if (!string.IsNullOrEmpty(website) || elapsedMs is null || elapsedMs < MinimumFillTime.TotalMilliseconds)
        {
            await audit.WriteAsync(new AuditEntry("dance-festival.registration-dropped", "DanceFestival", festival.Id.ToString()), cancellationToken);
            return null;
        }

        Validate(input, requireConsents: true);
        var token = NewToken();
        var registration = new DanceFestivalRegistration
        {
            Id = IdGenerator.NewId(),
            FestivalId = festival.Id,
            ClubName = input.ClubName.Trim(),
            ContactName = input.ContactName.Trim(),
            AddressLine = input.AddressLine.Trim(),
            PostalCode = input.PostalCode.Trim().ToUpperInvariant(),
            City = input.City.Trim(),
            Phone = input.Phone.Trim(),
            Email = input.Email.Trim().ToLowerInvariant(),
            Remarks = Clean(input.Remarks),
            PhotoConsent = input.PhotoConsent!.Value,
            PrivacyAcceptedAt = Now,
            RulesAcceptedAt = Now,
            SubmittedAt = Now,
            AccessTokenProtected = _tokens.Protect(token),
        };
        var order = 0;
        foreach (var entry in input.Entries)
        {
            registration.Entries.Add(NewEntry(registration.Id, entry, ++order));
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            registration.Number = await NextNumberAsync(festival.Id, cancellationToken);
            db.DanceFestivalRegistrations.Add(registration);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("dance-festival.registration-submitted", "DanceFestivalRegistration", registration.Id.ToString(), null,
            JsonSerializer.Serialize(new { registration.Number, dances = registration.Entries.Count }, Json)), cancellationToken);
        await SendAsync(FestivalMails.Confirmation(festival, registration, EditLink(baseUrl, registration.Id, token), PriceCents(festival, registration.Entries)), cancellationToken);
        await SendAsync(FestivalMails.NewRegistration(festival, registration, changed: false), cancellationToken);
        return registration.Number;
    }

    public async Task<FestivalOwnRegistrationView> OwnAsync(Guid id, string token, CancellationToken cancellationToken)
    {
        var (registration, festival) = await ByTokenAsync(id, token, cancellationToken);
        var payments = await PaymentsAsync(id, cancellationToken);
        return new FestivalOwnRegistrationView(
            registration.Id, registration.Number, registration.ClubName, registration.ContactName, registration.AddressLine, registration.PostalCode,
            registration.City, registration.Phone, registration.Email, registration.Remarks, registration.PhotoConsent, registration.InfoRequest,
            IsEditable(festival, registration), [.. registration.Entries.Where(e => e.RemovedAt is null).OrderBy(e => e.SortOrder).Select(ToView)],
            payments);
    }

    /// <summary>
    /// Wijziging door de vereniging. Tot de beoordeling gewoon aanpassen; na een beslissing moet een gewijzigde dans opnieuw
    /// beoordeeld worden (een goedgekeurde dans houdt zijn plek). De vorige versie blijft bewaard.
    /// </summary>
    public async Task UpdateOwnAsync(Guid id, string token, FestivalRegistrationInput input, CancellationToken cancellationToken)
    {
        var (registration, festival) = await ByTokenAsync(id, token, cancellationToken);
        if (!IsEditable(festival, registration))
        {
            throw new DomainException(ErrorCodes.FestivalClosed, "Wijzigen kan niet meer; neem contact op met de organisatie.", DomainErrorKind.Conflict);
        }

        Validate(input, requireConsents: false);
        var existing = registration.Entries.Where(e => e.RemovedAt is null).ToDictionary(e => e.Id);
        if (input.Entries.Any(e => e.Id is { } entryId && !existing.ContainsKey(entryId)))
        {
            throw new DomainException(ErrorCodes.Validation, "Een van de dansen bestaat niet (meer). Laad de pagina opnieuw.");
        }

        db.DanceFestivalRegistrationVersions.Add(new DanceFestivalRegistrationVersion
        {
            Id = IdGenerator.NewId(),
            RegistrationId = registration.Id,
            Version = registration.Version,
            Snapshot = JsonSerializer.Serialize(Snapshot(registration), Json),
            CreatedAt = Now,
        });

        registration.ClubName = input.ClubName.Trim();
        registration.ContactName = input.ContactName.Trim();
        registration.AddressLine = input.AddressLine.Trim();
        registration.PostalCode = input.PostalCode.Trim().ToUpperInvariant();
        registration.City = input.City.Trim();
        registration.Phone = input.Phone.Trim();
        registration.Email = input.Email.Trim().ToLowerInvariant();
        registration.Remarks = Clean(input.Remarks);
        if (input.PhotoConsent is { } consent)
        {
            registration.PhotoConsent = consent;
        }

        var keep = input.Entries.Where(e => e.Id is not null).Select(e => e.Id!.Value).ToHashSet();
        foreach (var removed in existing.Values.Where(e => !keep.Contains(e.Id)))
        {
            if (removed.Status == DanceEntryStatus.New)
            {
                registration.Entries.Remove(removed);
                db.DanceFestivalEntries.Remove(removed);
            }
            else
            {
                removed.RemovedAt = Now;
                removed.HoldsPlace = false;
                removed.WaitlistPosition = null;
            }
        }

        var order = 0;
        foreach (var entryInput in input.Entries)
        {
            order++;
            if (entryInput.Id is { } entryId)
            {
                var entry = existing[entryId];
                var changed = Apply(entry, entryInput);
                entry.SortOrder = order;
                if (changed && entry.Status is DanceEntryStatus.Approved or DanceEntryStatus.Waitlisted or DanceEntryStatus.Rejected)
                {
                    entry.Status = DanceEntryStatus.Changed;
                }
                else if (entry.Status == DanceEntryStatus.InfoRequested)
                {
                    // Antwoord op een vraag om meer informatie: weer ter beoordeling.
                    entry.Status = DanceEntryStatus.Changed;
                }
            }
            else
            {
                var entry = NewEntry(registration.Id, entryInput, order);
                registration.Entries.Add(entry);
                db.DanceFestivalEntries.Add(entry);
            }
        }

        registration.Version++;
        registration.ChangedAt = Now;
        registration.InfoRequest = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("dance-festival.registration-changed", "DanceFestivalRegistration", registration.Id.ToString(), null,
            JsonSerializer.Serialize(new { registration.Number, registration.Version }, Json)), cancellationToken);
        await SendAsync(FestivalMails.NewRegistration(festival, registration, changed: true), cancellationToken);
    }

    /// <summary>
    /// De betaallink uit de mail: staat de vorige Mollie-betaling nog open, dan dezelfde betaalpagina (geen tweede
    /// betaling); anders een nieuwe betaling.
    /// </summary>
    public async Task<string> PayAsync(Guid paymentId, string token, string baseUrl, CancellationToken cancellationToken)
    {
        var payment = await db.DanceFestivalPayments.SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Betaling niet gevonden.", DomainErrorKind.NotFound);
        var (registration, festival) = await ByTokenAsync(payment.RegistrationId, token, cancellationToken);
        var existing = payment.MolliePaymentId is { } previous ? await SyncAsync(payment, previous, cancellationToken) : null;
        if (payment.Status == DanceFestivalPaymentStatus.Paid)
        {
            throw new DomainException(ErrorCodes.OrderAlreadyPaid, "Dit bedrag is al betaald.", DomainErrorKind.Conflict);
        }

        if (payment.Status != DanceFestivalPaymentStatus.Open)
        {
            throw new DomainException(ErrorCodes.OrderNotPayable, "Deze betaallink is niet meer geldig.", DomainErrorKind.Conflict);
        }

        if (existing is { Status: "open", CheckoutUrl: { } checkout })
        {
            return checkout;
        }

        try
        {
            var created = await mollie.CreatePaymentAsync(new MollieNewPayment(
                payment.AmountCents, $"{festival.Name} · inschrijving {registration.Number}", EditLink(baseUrl, registration.Id, token) + "&betaald=1",
                WebhookUrl(baseUrl), payment.Id, $"{payment.Id:N}-{Now.Ticks}"), cancellationToken);
            payment.MolliePaymentId = created.Id;
            payment.MollieStatus = created.Status;
            db.DanceFestivalMolliePayments.Add(new DanceFestivalMolliePayment
            {
                Id = IdGenerator.NewId(),
                PaymentId = payment.Id,
                MolliePaymentId = created.Id,
                MollieStatus = created.Status,
                CreatedAt = Now,
            });
            await db.SaveChangesAsync(cancellationToken);
            return created.CheckoutUrl ?? throw new MollieException("Geen betaalpagina van Mollie.");
        }
        catch (Exception ex) when (ex is MollieException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogMollieFailed(logger, registration.Number, ex);
            throw new DomainException(ErrorCodes.PaymentFailed, "Betalen lukt nu niet. Probeer het over een paar minuten opnieuw.", DomainErrorKind.Conflict);
        }
    }

    /// <summary>
    /// Webhook van Mollie; geeft <c>false</c> als de betaling niet bij het festival hoort. Elke Mollie-betaling die ooit
    /// voor een betaalverzoek gestart is telt mee, niet alleen de laatste.
    /// </summary>
    public async Task<bool> HandleWebhookAsync(string molliePaymentId, CancellationToken cancellationToken)
    {
        var paymentId = await db.DanceFestivalMolliePayments.Where(p => p.MolliePaymentId == molliePaymentId)
            .Select(p => (Guid?)p.PaymentId).SingleOrDefaultAsync(cancellationToken);
        var payment = paymentId is null ? null : await db.DanceFestivalPayments.SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return false;
        }

        await SyncAsync(payment, molliePaymentId, cancellationToken);
        return true;
    }

    // ---- Beheer ------------------------------------------------------------------------------------------------------

    public async Task<FestivalSettingsView?> SettingsAsync(CancellationToken cancellationToken) =>
        await ActiveAsync(cancellationToken) is { } festival ? ToSettingsView(festival) : null;

    public async Task<FestivalSettingsView> SaveSettingsAsync(FestivalSettingsInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Location) || input.Capacity < 0
            || input.PriceSoloDuoCents < 0 || input.PriceGroupOneDanceCents < 0 || input.PriceGroupTwoDancesCents < 0
            || (input.RegistrationOpensAt is { } from && input.RegistrationClosesAt is { } to && to <= from))
        {
            throw new DomainException(ErrorCodes.Validation, "Controleer de instellingen: naam, locatie, plekken, tarieven en de inschrijfperiode.");
        }

        if (!System.Net.Mail.MailAddress.TryCreate(input.NotificationEmail.Trim(), out _))
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een geldig e-mailadres in voor de meldingen.");
        }

        if (input.VatPercent is not (0 or 9 or 21) || input.ServiceFeeCents is < 0 or > 100000)
        {
            throw new DomainException(ErrorCodes.Validation, "Btw-tarief is 0, 9 of 21%, en servicekosten zijn hooguit € 1000,00.");
        }

        var festival = await db.DanceFestivals.SingleOrDefaultAsync(f => f.Active, cancellationToken);
        if (festival is null)
        {
            festival = new DanceFestival { Id = IdGenerator.NewId(), Name = "", Location = "", NotificationEmail = "", Active = true };
            db.DanceFestivals.Add(festival);
        }

        festival.Name = input.Name.Trim();
        festival.FestivalDate = input.FestivalDate;
        festival.Location = input.Location.Trim();
        festival.PageEnabled = input.PageEnabled;
        festival.RegistrationOpensAt = input.RegistrationOpensAt;
        festival.RegistrationClosesAt = input.RegistrationClosesAt;
        festival.Capacity = input.Capacity;
        festival.PaymentsEnabled = input.PaymentsEnabled;
        festival.PriceSoloDuoCents = input.PriceSoloDuoCents;
        festival.PriceGroupOneDanceCents = input.PriceGroupOneDanceCents;
        festival.PriceGroupTwoDancesCents = input.PriceGroupTwoDancesCents;
        festival.VatRate = input.VatPercent / 100m;
        festival.ServiceFeeOn = input.ServiceFeeOn;
        festival.ServiceFeeCents = input.ServiceFeeCents;
        festival.NotificationEmail = input.NotificationEmail.Trim().ToLowerInvariant();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("dance-festival.settings-changed", "DanceFestival", festival.Id.ToString(), null,
            JsonSerializer.Serialize(input, Json)), cancellationToken);
        return ToSettingsView(festival);
    }

    /// <summary>Reglement of privacyverklaring (PDF) voor de actieve editie.</summary>
    public async Task SetDocumentAsync(string kind, UploadedFile file, CancellationToken cancellationToken)
    {
        var festival = await db.DanceFestivals.SingleOrDefaultAsync(f => f.Active, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Sla eerst de instellingen van het festival op.", DomainErrorKind.NotFound);
        if (kind is not ("reglement" or "privacyverklaring"))
        {
            throw new DomainException(ErrorCodes.Validation, "Onbekend document.");
        }

        await using var stream = file.Open();
        var stored = await contentFiles.StoreAttachmentAsync(stream, file.Length, "dansgardefestival", cancellationToken);
        if (stored.Kind != FileKind.Pdf)
        {
            await files.DeleteAsync(FileContainers.Content, stored.Path, cancellationToken);
            throw new DomainException(ErrorCodes.FileTypeNotAllowed, "Alleen een PDF is toegestaan.");
        }

        if (kind == "reglement")
        {
            festival.RulesBlobPath = stored.Path;
        }
        else
        {
            festival.PrivacyBlobPath = stored.Path;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("dance-festival.document-uploaded", "DanceFestival", festival.Id.ToString(), null, kind), cancellationToken);
    }

    public async Task<FestivalAdminList> ListAsync(CancellationToken cancellationToken)
    {
        var festival = await ActiveAsync(cancellationToken);
        if (festival is null)
        {
            return new FestivalAdminList(null, new FestivalStats(0, 0, 0, 0, 0, 0), []);
        }

        var registrations = await db.DanceFestivalRegistrations.AsNoTracking().Include(r => r.Entries)
            .Where(r => r.FestivalId == festival.Id).OrderBy(r => r.Number).ToListAsync(cancellationToken);
        var ids = registrations.Select(r => r.Id).ToList();
        var payments = (await db.DanceFestivalPayments.AsNoTracking().Where(p => ids.Contains(p.RegistrationId)).ToListAsync(cancellationToken))
            .ToLookup(p => p.RegistrationId);
        var paymentIds = payments.SelectMany(g => g).Select(p => p.Id).ToList();
        var duplicates = (await db.DanceFestivalMolliePayments.AsNoTracking().Where(m => m.DuplicatePaid && paymentIds.Contains(m.PaymentId))
            .Select(m => m.PaymentId).ToListAsync(cancellationToken)).ToHashSet();
        var summaries = registrations.Select(r =>
        {
            var entries = r.Entries.Where(e => e.RemovedAt is null).ToList();
            var toReview = entries.Count(e => e.Status is DanceEntryStatus.New or DanceEntryStatus.Changed);
            var waitlist = entries.Where(e => e.Status == DanceEntryStatus.Waitlisted).ToList();
            return new FestivalRegistrationSummary(
                r.Id, r.Number, r.ClubName, r.ContactName, entries.Count, entries.Count(e => e.Status == DanceEntryStatus.Approved), toReview, waitlist.Count,
                waitlist.Min(e => e.WaitlistPosition), SummaryStatus(entries), PaymentSummary(payments[r.Id].ToList(), duplicates), r.SubmittedAt, r.ChangedAt);
        }).ToList();
        var allEntries = registrations.SelectMany(r => r.Entries).Where(e => e.RemovedAt is null).ToList();
        var allPayments = payments.SelectMany(g => g).Where(p => p.Status != DanceFestivalPaymentStatus.Canceled).ToList();
        var stats = new FestivalStats(
            festival.Capacity, allEntries.Count(e => e.HoldsPlace), allEntries.Count(e => e.Status is DanceEntryStatus.New or DanceEntryStatus.Changed),
            allEntries.Count(e => e.Status == DanceEntryStatus.Waitlisted), allPayments.Where(p => p.Status == DanceFestivalPaymentStatus.Paid).Sum(p => p.AmountCents),
            allPayments.Sum(p => p.AmountCents));
        return new FestivalAdminList(ToSettingsView(festival), stats, summaries);
    }

    public async Task<FestivalRegistrationDetail> DetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var registration = await db.DanceFestivalRegistrations.AsNoTracking().Include(r => r.Entries).SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);
        var festival = await db.DanceFestivals.AsNoTracking().SingleAsync(f => f.Id == registration.FestivalId, cancellationToken);
        var versions = await db.DanceFestivalRegistrationVersions.AsNoTracking().Where(v => v.RegistrationId == id).OrderBy(v => v.Version)
            .Select(v => new FestivalVersionInfo(v.Version, v.CreatedAt)).ToListAsync(cancellationToken);
        var payments = await PaymentsAsync(id, cancellationToken);
        var approved = registration.Entries.Where(e => e.RemovedAt is null && e.HoldsPlace).ToList();
        var outstanding = payments.Where(p => p.Status != DanceFestivalPaymentStatus.Canceled).Sum(p => p.AmountCents);
        return new FestivalRegistrationDetail(
            registration.Id, registration.Number, registration.ClubName, registration.ContactName, registration.AddressLine, registration.PostalCode,
            registration.City, registration.Phone, registration.Email, registration.Remarks, registration.PhotoConsent, registration.PrivacyAcceptedAt,
            registration.RulesAcceptedAt, registration.SubmittedAt, registration.ChangedAt, registration.Version, registration.InfoRequest,
            [.. registration.Entries.OrderBy(e => e.RemovedAt is null ? 0 : 1).ThenBy(e => e.SortOrder).Select(ToView)], versions, payments,
            Math.Max(0, PriceCents(festival, approved) - outstanding));
    }

    /// <summary>Een eerdere versie (JSON zoals bewaard).</summary>
    public async Task<string> VersionAsync(Guid id, int version, CancellationToken cancellationToken) =>
        await db.DanceFestivalRegistrationVersions.AsNoTracking().Where(v => v.RegistrationId == id && v.Version == version)
            .Select(v => v.Snapshot).SingleOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.NotFound, "Versie niet gevonden.", DomainErrorKind.NotFound);

    /// <summary>
    /// Beslissing over één of meer dansen van een inschrijving; de vereniging krijgt één mail. Goedkeuren vult een plek; is
    /// alles vol, dan eerst een plek vrijmaken of het aantal plekken verhogen.
    /// </summary>
    public async Task DecideAsync(Guid registrationId, IReadOnlyCollection<Guid> entryIds, FestivalDecision decision, string? message, string baseUrl,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies minstens één dans.");
        }

        if (decision == FestivalDecision.RequestInfo && string.IsNullOrWhiteSpace(message))
        {
            throw new DomainException(ErrorCodes.Validation, "Schrijf welke informatie je nodig hebt.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var registration = await db.DanceFestivalRegistrations.Include(r => r.Entries).SingleOrDefaultAsync(r => r.Id == registrationId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);
        var festival = await LockFestivalAsync(registration.FestivalId, cancellationToken);
        var entries = registration.Entries.Where(e => entryIds.Contains(e.Id) && e.RemovedAt is null).ToList();
        if (entries.Count != entryIds.Count)
        {
            throw new DomainException(ErrorCodes.NotFound, "Een van de dansen bestaat niet (meer).", DomainErrorKind.NotFound);
        }

        if (decision == FestivalDecision.Approve)
        {
            var needed = entries.Count(e => !e.HoldsPlace);
            var taken = await PlacesTakenAsync(festival.Id, cancellationToken);
            if (taken + needed > festival.Capacity)
            {
                throw new DomainException(ErrorCodes.FestivalFull,
                    $"Niet genoeg plekken: {festival.Capacity - taken} vrij, {needed} nodig. Zet dansen op de wachtlijst of verhoog het aantal plekken.",
                    DomainErrorKind.Conflict);
            }
        }

        var nextPosition = decision == FestivalDecision.Waitlist ? await NextWaitlistPositionAsync(festival.Id, cancellationToken) : 0;
        foreach (var entry in entries)
        {
            entry.DecidedAt = Now;
            switch (decision)
            {
                case FestivalDecision.Approve:
                    entry.Status = DanceEntryStatus.Approved;
                    entry.HoldsPlace = true;
                    entry.WaitlistPosition = null;
                    break;
                case FestivalDecision.RequestInfo:
                    entry.Status = DanceEntryStatus.InfoRequested;
                    break;
                case FestivalDecision.Waitlist:
                    entry.Status = DanceEntryStatus.Waitlisted;
                    entry.HoldsPlace = false;
                    entry.WaitlistPosition ??= nextPosition++;
                    break;
                case FestivalDecision.Reject:
                    entry.Status = DanceEntryStatus.Rejected;
                    entry.HoldsPlace = false;
                    entry.WaitlistPosition = null;
                    break;
            }
        }

        if (decision == FestivalDecision.RequestInfo)
        {
            registration.InfoRequest = message!.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        await CompactWaitlistAsync(festival.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("dance-festival.decided", "DanceFestivalRegistration", registration.Id.ToString(), null,
            JsonSerializer.Serialize(new { decision = decision.ToString(), entries = entryIds, message }, Json)), cancellationToken);
        await SendAsync(FestivalMails.Decision(festival, registration, entries, decision, message?.Trim(),
            EditLink(baseUrl, registration.Id, _tokens.Unprotect(registration.AccessTokenProtected))), cancellationToken);
    }

    /// <summary>Een dans op de wachtlijst één plaats omhoog (<c>-1</c>) of omlaag (<c>+1</c>).</summary>
    public async Task MoveWaitlistAsync(Guid entryId, int direction, CancellationToken cancellationToken)
    {
        var entry = await db.DanceFestivalEntries.SingleOrDefaultAsync(e => e.Id == entryId && e.Status == DanceEntryStatus.Waitlisted, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Deze dans staat niet op de wachtlijst.", DomainErrorKind.NotFound);
        var festivalId = await db.DanceFestivalRegistrations.Where(r => r.Id == entry.RegistrationId).Select(r => r.FestivalId).SingleAsync(cancellationToken);
        var waitlist = await WaitlistQuery(festivalId).OrderBy(e => e.WaitlistPosition).ToListAsync(cancellationToken);
        var index = waitlist.FindIndex(e => e.Id == entryId);
        var other = index + Math.Sign(direction);
        if (other < 0 || other >= waitlist.Count)
        {
            return;
        }

        (waitlist[index].WaitlistPosition, waitlist[other].WaitlistPosition) = (waitlist[other].WaitlistPosition, waitlist[index].WaitlistPosition);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("dance-festival.waitlist-moved", "DanceFestivalEntry", entryId.ToString(), null, direction.ToString(System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);
    }

    /// <summary>Betaallink sturen voor een bedrag (na goedkeuring); de status volgt via Mollie.</summary>
    public async Task<Guid> RequestPaymentAsync(Guid registrationId, int amountCents, string baseUrl, CancellationToken cancellationToken)
    {
        var registration = await db.DanceFestivalRegistrations.Include(r => r.Entries).SingleOrDefaultAsync(r => r.Id == registrationId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);
        var festival = await db.DanceFestivals.AsNoTracking().SingleAsync(f => f.Id == registration.FestivalId, cancellationToken);
        if (!festival.PaymentsEnabled)
        {
            throw new DomainException(ErrorCodes.PaymentsNotConfigured, "Betalen via iDEAL staat uit (Instellingen).", DomainErrorKind.Conflict);
        }

        if (!registration.Entries.Any(e => e.RemovedAt is null && e.Status == DanceEntryStatus.Approved))
        {
            throw new DomainException(ErrorCodes.Validation, "Een betaallink kan pas na goedkeuring van minstens één dans.", DomainErrorKind.Conflict);
        }

        if (amountCents is <= 0 or > 1_000_000)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een bedrag in tussen € 0,01 en € 10.000.");
        }

        // Het ingevulde bedrag is exclusief btw; btw (instelbaar tarief) en eventuele servicekosten (zelfde tarief) komen
        // erbovenop. Het totaal is wat bij Mollie wordt afgerekend. Afronden per regel, zodat de btw op de factuur optelt.
        var vat = (int)Math.Round(amountCents * festival.VatRate, MidpointRounding.AwayFromZero);
        var feeNet = festival.ServiceFeeOn ? festival.ServiceFeeCents : 0;
        var feeVat = (int)Math.Round(feeNet * festival.VatRate, MidpointRounding.AwayFromZero);
        var total = amountCents + vat + feeNet + feeVat;
        var year = festival.FestivalDate.Year;
        var seq = await db.DanceFestivalPayments.CountAsync(p => p.InvoiceNumber != null && p.InvoiceNumber.StartsWith($"FEST-{year}-"), cancellationToken) + 1;

        var payment = new DanceFestivalPayment
        {
            Id = IdGenerator.NewId(),
            RegistrationId = registration.Id,
            AmountCents = total,
            NetCents = amountCents,
            VatCents = vat,
            ServiceFeeNetCents = feeNet,
            ServiceFeeVatCents = feeVat,
            InvoiceNumber = $"FEST-{year.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{seq.ToString("0000", System.Globalization.CultureInfo.InvariantCulture)}",
            Status = DanceFestivalPaymentStatus.Open,
            CreatedAt = Now,
        };
        db.DanceFestivalPayments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("dance-festival.payment-requested", "DanceFestivalPayment", payment.Id.ToString(), null,
            JsonSerializer.Serialize(new { registration.Number, net = amountCents, total }, Json)), cancellationToken);
        var token = _tokens.Unprotect(registration.AccessTokenProtected);
        await SendAsync(FestivalMails.PaymentLink(festival, registration, total, PayLink(baseUrl, payment.Id, token)), cancellationToken);
        return payment.Id;
    }

    /// <summary>Status bij Mollie opnieuw ophalen (de webhook doet dit ook vanzelf).</summary>
    public async Task RefreshPaymentAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await db.DanceFestivalPayments.SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Betaling niet gevonden.", DomainErrorKind.NotFound);
        if (payment.MolliePaymentId is { } molliePaymentId)
        {
            await SyncAsync(payment, molliePaymentId, cancellationToken);
        }
    }

    /// <summary>Een openstaand betaalverzoek intrekken (bijv. verkeerd bedrag).</summary>
    public async Task CancelPaymentAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await db.DanceFestivalPayments.SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Betaling niet gevonden.", DomainErrorKind.NotFound);
        if (payment.MolliePaymentId is { } molliePaymentId)
        {
            await SyncAsync(payment, molliePaymentId, cancellationToken);
        }

        if (payment.Status != DanceFestivalPaymentStatus.Open)
        {
            throw new DomainException(ErrorCodes.OrderNotPayable, "Alleen een openstaande betaling kan worden ingetrokken.", DomainErrorKind.Conflict);
        }

        payment.Status = DanceFestivalPaymentStatus.Canceled;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("dance-festival.payment-canceled", "DanceFestivalPayment", payment.Id.ToString()), cancellationToken);
    }

    /// <summary>Alle dansen als CSV (Excel, puntkomma).</summary>
    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken)
    {
        var festival = await ActiveAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Er is geen festival.", DomainErrorKind.NotFound);
        var rows = await db.DanceFestivalRegistrations.AsNoTracking().Include(r => r.Entries).Where(r => r.FestivalId == festival.Id)
            .OrderBy(r => r.Number).ToListAsync(cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine("Nr;Vereniging;Contactpersoon;Telefoon;E-mail;Foto/video;Soort;Vorm;Categorie;Naam/namen;Gem. leeftijd;Dansers;Leiding;Titel;Karakter/modern;Status;Wachtlijst");
        foreach (var r in rows)
        {
            foreach (var e in r.Entries.Where(e => e.RemovedAt is null).OrderBy(e => e.SortOrder))
            {
                string[] cells =
                [
                    r.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), r.ClubName, r.ContactName, r.Phone, r.Email, r.PhotoConsent ? "ja" : "nee",
                    FestivalMails.TypeLabel(e.DanceType), FestivalMails.FormatLabel(e.Format), e.Category.ToString(), e.Names,
                    e.AverageAge.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("nl-NL")), e.DancerCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    e.LeaderName, e.Title ?? "", e.ShowStyle is { } style ? FestivalMails.StyleLabel(style) : "", FestivalMails.StatusLabel(e.Status),
                    e.WaitlistPosition?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                ];
                csv.AppendLine(Csv.Row(cells));
            }
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    // ---- Hulpfuncties ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Inschrijfgeld: solo of duo per dans; een groep met één dans het groepstarief, met twee dansen (garde + show, zelfde
    /// groepsnaam) het tarief voor twee dansen.
    /// </summary>
    public static int PriceCents(DanceFestival festival, IEnumerable<DanceFestivalEntry> entries)
    {
        var list = entries.Where(e => e.RemovedAt is null).ToList();
        var soloDuo = list.Count(e => e.Format != DanceFormat.Group) * festival.PriceSoloDuoCents;
        var groups = list.Where(e => e.Format == DanceFormat.Group).GroupBy(e => e.Names.Trim().ToLowerInvariant())
            .Sum(g => (g.Count() / 2 * festival.PriceGroupTwoDancesCents) + (g.Count() % 2 * festival.PriceGroupOneDanceCents));
        return soloDuo + groups;
    }

    /// <summary>Peildatum leeftijd: 1 januari vóór de festivaldatum.</summary>
    public static DateOnly AgeReferenceDate(DanceFestival festival) => new(festival.FestivalDate.Year, 1, 1);

    private FestivalState StateOf(DanceFestival festival) =>
        !festival.PageEnabled ? FestivalState.Hidden
        : festival.RegistrationOpensAt is not { } from || Now < from ? FestivalState.NotYetOpen
        : festival.RegistrationClosesAt is { } to && Now >= to ? FestivalState.Closed
        : FestivalState.Open;

    private bool IsEditable(DanceFestival festival, DanceFestivalRegistration registration) =>
        StateOf(festival) == FestivalState.Open
        || registration.Entries.Any(e => e.RemovedAt is null && e.Status == DanceEntryStatus.InfoRequested);

    private Task<DanceFestival?> ActiveAsync(CancellationToken cancellationToken) =>
        db.DanceFestivals.AsNoTracking().SingleOrDefaultAsync(f => f.Active, cancellationToken);

    private FestivalSettingsView ToSettingsView(DanceFestival f) => new(
        f.Id, f.Name, f.FestivalDate, f.Location, f.PageEnabled, f.RegistrationOpensAt, f.RegistrationClosesAt, f.Capacity, f.PaymentsEnabled,
        f.PriceSoloDuoCents, f.PriceGroupOneDanceCents, f.PriceGroupTwoDancesCents, f.NotificationEmail, f.RulesBlobPath is not null,
        f.PrivacyBlobPath is not null, StateOf(f), (int)Math.Round(f.VatRate * 100m), f.ServiceFeeOn, f.ServiceFeeCents);

    private Task<int> PlacesTakenAsync(Guid festivalId, CancellationToken cancellationToken) =>
        db.DanceFestivalEntries.Where(e => e.HoldsPlace && e.RemovedAt == null
            && db.DanceFestivalRegistrations.Any(r => r.Id == e.RegistrationId && r.FestivalId == festivalId)).CountAsync(cancellationToken);

    private IQueryable<DanceFestivalEntry> WaitlistQuery(Guid festivalId) =>
        db.DanceFestivalEntries.Where(e => e.Status == DanceEntryStatus.Waitlisted && e.RemovedAt == null
            && db.DanceFestivalRegistrations.Any(r => r.Id == e.RegistrationId && r.FestivalId == festivalId));

    private async Task<int> NextWaitlistPositionAsync(Guid festivalId, CancellationToken cancellationToken) =>
        (await WaitlistQuery(festivalId).MaxAsync(e => e.WaitlistPosition, cancellationToken) ?? 0) + 1;

    /// <summary>Wachtlijst weer 1, 2, 3 … (na goedkeuren of afwijzen van een dans van de wachtlijst).</summary>
    private async Task CompactWaitlistAsync(Guid festivalId, CancellationToken cancellationToken)
    {
        var waitlist = await WaitlistQuery(festivalId).OrderBy(e => e.WaitlistPosition).ToListAsync(cancellationToken);
        for (var i = 0; i < waitlist.Count; i++)
        {
            waitlist[i].WaitlistPosition = i + 1;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DanceFestival> LockFestivalAsync(Guid festivalId, CancellationToken cancellationToken)
    {
        // Eén beslissing tegelijk per editie: de plekken kunnen niet dubbel vergeven worden.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM content.DanceFestival WITH (UPDLOCK, HOLDLOCK) WHERE id = {festivalId}", cancellationToken);
        return await db.DanceFestivals.AsNoTracking().SingleAsync(f => f.Id == festivalId, cancellationToken);
    }

    private async Task<int> NextNumberAsync(Guid festivalId, CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<int>($"""
            UPDATE content.DanceFestival SET last_registration_number = last_registration_number + 1
            OUTPUT inserted.last_registration_number AS [Value] WHERE id = {festivalId}
            """).ToListAsync(cancellationToken)).Single();

    private async Task<(DanceFestivalRegistration Registration, DanceFestival Festival)> ByTokenAsync(Guid id, string token, CancellationToken cancellationToken)
    {
        var registration = await db.DanceFestivalRegistrations.Include(r => r.Entries).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (registration is null || !Matches(registration, token))
        {
            throw new DomainException(ErrorCodes.NotFound, "Deze link is niet (meer) geldig.", DomainErrorKind.NotFound);
        }

        var festival = await db.DanceFestivals.AsNoTracking().SingleAsync(f => f.Id == registration.FestivalId, cancellationToken);
        return (registration, festival);
    }

    private bool Matches(DanceFestivalRegistration registration, string token)
    {
        try
        {
            var expected = Encoding.UTF8.GetBytes(_tokens.Unprotect(registration.AccessTokenProtected));
            return CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(token ?? ""));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<FestivalPaymentView>> PaymentsAsync(Guid registrationId, CancellationToken cancellationToken) =>
        await db.DanceFestivalPayments.AsNoTracking().Where(p => p.RegistrationId == registrationId).OrderBy(p => p.CreatedAt)
            .Select(p => new FestivalPaymentView(p.Id, p.AmountCents, p.Status, p.MollieStatus, p.CreatedAt, p.PaidAt,
                db.DanceFestivalMolliePayments.Count(m => m.PaymentId == p.Id && m.DuplicatePaid)))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Haalt een Mollie-betaling op en werkt het betaalverzoek bij (idempotent). Een tweede betaalde Mollie-betaling voor
    /// hetzelfde verzoek wordt niet nog eens geboekt, maar gemarkeerd om terug te betalen.
    /// </summary>
    private async Task<MolliePayment?> SyncAsync(DanceFestivalPayment payment, string molliePaymentId, CancellationToken cancellationToken)
    {
        var mollieResult = await mollie.GetPaymentAsync(molliePaymentId, cancellationToken);
        if (mollieResult.OrderId != payment.Id)
        {
            return null;
        }

        var attempt = await db.DanceFestivalMolliePayments.SingleOrDefaultAsync(m => m.MolliePaymentId == molliePaymentId, cancellationToken);
        if (attempt is not null)
        {
            attempt.MollieStatus = mollieResult.Status;
        }

        if (payment.MolliePaymentId == molliePaymentId || mollieResult.IsPaid)
        {
            payment.MollieStatus = mollieResult.Status;
        }

        // Zonder eigen regel (betaling van vóór 2026-10-17) telt alleen de status van het verzoek.
        var handled = attempt is null ? payment.Status == DanceFestivalPaymentStatus.Paid : attempt.PaidAt is not null;
        if (mollieResult.IsPaid && !handled && mollieResult.AmountCents == payment.AmountCents)
        {
            if (attempt is not null)
            {
                attempt.PaidAt = mollieResult.PaidAt ?? Now;
            }

            if (payment.Status == DanceFestivalPaymentStatus.Paid && attempt is not null)
            {
                attempt.DuplicatePaid = true;

                await db.SaveChangesAsync(cancellationToken);
                await audit.WriteAsync(new AuditEntry("dance-festival.payment-paid-twice", "DanceFestivalPayment", payment.Id.ToString(), null,
                    JsonSerializer.Serialize(new { molliePaymentId, payment.AmountCents }, Json)), cancellationToken);
                LogPaidTwice(logger, payment.Id, molliePaymentId);
                return mollieResult;
            }

            // Ook een ingetrokken verzoek dat toch betaald is, telt als betaald.
            payment.Status = DanceFestivalPaymentStatus.Paid;
            payment.PaidAt = mollieResult.PaidAt ?? Now;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("dance-festival.payment-paid", "DanceFestivalPayment", payment.Id.ToString(), null,
                JsonSerializer.Serialize(new { molliePaymentId, payment.AmountCents }, Json)), cancellationToken);
            return mollieResult;
        }

        await db.SaveChangesAsync(cancellationToken);
        return mollieResult;
    }

    private void Validate(FestivalRegistrationInput input, bool requireConsents)
    {
        var issues = new List<string>();
        void Required(string? value, string label, int max)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
            {
                issues.Add(label);
            }
        }

        Required(input.ClubName, "naam vereniging", 150);
        Required(input.ContactName, "naam contactpersoon", 100);
        Required(input.AddressLine, "adres", 200);
        Required(input.PostalCode, "postcode", 10);
        Required(input.City, "plaats", 100);
        Required(input.Phone, "telefoon", 30);
        if (!System.Net.Mail.MailAddress.TryCreate(input.Email?.Trim(), out _) || input.Email.Trim().Length > 254)
        {
            issues.Add("e-mailadres");
        }
        else if (EmailSenderAddress.IsOwnDomain(input.Email, emailOptions.Value))
        {
            // Het adres wordt het antwoordadres van de melding aan de organisatie; een eigen adres zou die laten lijken
            // op een mail van de vereniging zelf.
            issues.Add("e-mailadres (gebruik het adres van je eigen vereniging, niet een adres van De Vrolijke Drammers)");
        }

        if (input.Remarks?.Trim().Length > 2000)
        {
            issues.Add("opmerkingen (maximaal 2000 tekens)");
        }

        if (requireConsents)
        {
            if (input.PhotoConsent is null)
            {
                issues.Add("toestemming voor foto's en video's (ja of nee)");
            }

            if (!input.PrivacyAccepted)
            {
                issues.Add("de privacyverklaring gelezen");
            }

            if (!input.RulesAccepted)
            {
                issues.Add("akkoord met het reglement");
            }
        }

        if (input.Entries is null || input.Entries.Count == 0 || input.Entries.Count > MaxEntries)
        {
            issues.Add($"minstens één dans (maximaal {MaxEntries})");
        }
        else
        {
            var n = 0;
            foreach (var e in input.Entries)
            {
                n++;
                var label = $"dans {n}";
                if (string.IsNullOrWhiteSpace(e.Names) || e.Names.Trim().Length > 300)
                {
                    issues.Add($"{label}: naam of namen");
                }

                if (string.IsNullOrWhiteSpace(e.LeaderName) || e.LeaderName.Trim().Length > 100)
                {
                    issues.Add($"{label}: naam leiding");
                }

                if (e.AverageAge is < 2 or > 99)
                {
                    issues.Add($"{label}: gemiddelde leeftijd");
                }

                var dancersOk = e.Format switch { DanceFormat.Solo => e.DancerCount == 1, DanceFormat.Duo => e.DancerCount == 2, _ => e.DancerCount is >= 3 and <= 200 };
                if (!dancersOk)
                {
                    issues.Add($"{label}: aantal dansers (solo 1, duo 2, groep minstens 3)");
                }

                if (e.DanceType == DanceType.Show && (string.IsNullOrWhiteSpace(e.Title) || e.Title.Trim().Length > 150 || e.ShowStyle is null))
                {
                    issues.Add($"{label}: titel en karakter of modern");
                }
            }
        }

        if (issues.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Controleer: " + string.Join(", ", issues) + ".");
        }
    }

    private static DanceFestivalEntry NewEntry(Guid registrationId, FestivalEntryInput input, int order)
    {
        var entry = new DanceFestivalEntry { Id = IdGenerator.NewId(), RegistrationId = registrationId, Names = "", LeaderName = "", Status = DanceEntryStatus.New };
        Apply(entry, input);
        entry.SortOrder = order;
        return entry;
    }

    /// <summary>Neemt de invoer over; geeft <c>true</c> als er iets veranderd is.</summary>
    private static bool Apply(DanceFestivalEntry entry, FestivalEntryInput input)
    {
        var title = input.DanceType == DanceType.Show ? input.Title?.Trim() : null;
        var style = input.DanceType == DanceType.Show ? input.ShowStyle : null;
        var age = Math.Round(input.AverageAge, 1);
        var changed = entry.DanceType != input.DanceType || entry.Format != input.Format || entry.Names != input.Names.Trim()
            || entry.AverageAge != age || entry.DancerCount != input.DancerCount || entry.LeaderName != input.LeaderName.Trim()
            || entry.Title != title || entry.ShowStyle != style;
        entry.DanceType = input.DanceType;
        entry.Format = input.Format;
        entry.Names = input.Names.Trim();
        entry.AverageAge = age;
        entry.DancerCount = input.DancerCount;
        entry.LeaderName = input.LeaderName.Trim();
        entry.Title = title;
        entry.ShowStyle = style;
        entry.Category = DanceFestivalEntry.CategoryFor(age);
        return changed;
    }

    private static FestivalEntryView ToView(DanceFestivalEntry e) => new(
        e.Id, e.SortOrder, e.DanceType, e.Format, e.Names, e.AverageAge, e.DancerCount, e.LeaderName, e.Title, e.ShowStyle, e.Category, e.Status,
        e.HoldsPlace, e.WaitlistPosition, e.RemovedAt);

    private static object Snapshot(DanceFestivalRegistration r) => new
    {
        r.Version,
        r.ClubName,
        r.ContactName,
        r.AddressLine,
        r.PostalCode,
        r.City,
        r.Phone,
        r.Email,
        r.Remarks,
        r.PhotoConsent,
        r.ChangedAt,
        Entries = r.Entries.Where(e => e.RemovedAt is null).OrderBy(e => e.SortOrder).Select(ToView).ToList(),
    };

    private static string SummaryStatus(List<DanceFestivalEntry> entries) =>
        entries.Count == 0 ? "Ingetrokken"
        : entries.Any(e => e.Status == DanceEntryStatus.Changed) ? "Gewijzigd"
        : entries.Any(e => e.Status == DanceEntryStatus.New) ? "Nieuw"
        : entries.Any(e => e.Status == DanceEntryStatus.InfoRequested) ? "Meer info gevraagd"
        : entries.All(e => e.Status == DanceEntryStatus.Rejected) ? "Afgewezen"
        : entries.Any(e => e.Status == DanceEntryStatus.Approved) && entries.Any(e => e.Status == DanceEntryStatus.Waitlisted) ? "Deels wachtlijst"
        : entries.Any(e => e.Status == DanceEntryStatus.Approved) ? "Goedgekeurd"
        : "Wachtlijst";

    private static string PaymentSummary(List<DanceFestivalPayment> payments, HashSet<Guid> duplicates)
    {
        if (payments.Any(p => duplicates.Contains(p.Id)))
        {
            return "Dubbel betaald";
        }

        var active = payments.Where(p => p.Status != DanceFestivalPaymentStatus.Canceled).ToList();
        return active.Count == 0 ? "—" : active.All(p => p.Status == DanceFestivalPaymentStatus.Paid) ? "Betaald" : "Link verstuurd";
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string EditLink(string baseUrl, Guid id, string token) => $"{baseUrl}/dansgardefestival/?inschrijving={id}&t={token}";

    public static string PayLink(string baseUrl, Guid paymentId, string token) => $"{baseUrl}/api/v1/dance-festival/payments/{paymentId}/pay?t={token}";

    private static string? WebhookUrl(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && !uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttps
            ? $"{baseUrl}/api/v1/payments/mollie/webhook"
            : null;

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Mails mogen een inschrijving of beslissing niet laten mislukken; een fout komt in de log.</summary>
    private async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogMailFailed(logger, message.Subject, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mollie-betaling voor festivalinschrijving {Number} mislukt")]
    private static partial void LogMollieFailed(ILogger logger, int number, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Betaalverzoek {PaymentId} van het Dansgardefestival is dubbel betaald ({MolliePaymentId}); terugbetalen")]
    private static partial void LogPaidTwice(ILogger logger, Guid paymentId, string molliePaymentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mail van het Dansgardefestival niet verstuurd: {Subject}")]
    private static partial void LogMailFailed(ILogger logger, string subject, Exception exception);
}
