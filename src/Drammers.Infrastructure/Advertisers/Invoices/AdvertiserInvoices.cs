using System.Globalization;
using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Mailings;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Payments;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Advertisers;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Advertisers.Invoices;

public sealed record InvoiceSettings(
    string? Address, string? Kvk, string? VatNumber = null, string? Phone = null, string? Website = null, bool ServiceFeeOn = false, int ServiceFeeCents = 0,
    int PaymentTermDays = 14);

/// <summary>Een adverteerder in het factuuroverzicht van een jaar, met of zonder factuur.</summary>
public sealed record InvoiceOverviewRow(
    Guid AdvertiserId, int AdvertiserNumber, string CompanyName, decimal Amount, AdvertiserPayment Payment, string? Email, Guid? InvoiceId,
    string? InvoiceNumber, DateOnly? InvoiceDate, DateTime? SentAt, InvoiceStatus? Status = null, InvoicePaymentMethod? PaymentMethod = null,
    decimal? TotalInclVat = null, string? MollieStatus = null, DateTime? PaidAt = null, DateOnly? DueDate = null, bool Overdue = false);

public sealed record InvoiceOverview(int Year, int ToCreate, int ToSend, int WithoutEmail, IReadOnlyList<InvoiceOverviewRow> Rows);

/// <summary>
/// Facturen aan adverteerders (fase 27e): per campagnejaar één factuur voor elke opgehaalde bijdrage (niet gratis), met
/// een vast nummer ADV-jaar-volgnummer. Versturen als PDF per e-mail namens de penningmeester; wie geen e-mailadres heeft,
/// krijgt een PDF om te printen.
/// </summary>
public sealed class AdvertiserInvoices(
    DrammersDbContext db, SepaCollections collections, IOutbox outbox, IAuditLogger audit, IClock clock, ICurrentActor actor,
    IOptions<MailingOptions> mailingOptions, IMollieClient mollie, IDataProtectionProvider protection, ILogger<AdvertiserInvoices> logger)
{
    public const string MailMessageType = "advertiser.invoice-mail";

    private readonly IDataProtector _tokens = protection.CreateProtector("Drammers.AdvertiserInvoiceToken.v1");

    /// <summary>Het KvK-nummer uit de oude conceptfactuur, als er nog niets is ingesteld.</summary>
    public const string DefaultKvk = "40122564";

    /// <summary>Btw-tarief op advertenties en servicekosten (21%).</summary>
    public const decimal Vat21 = 0.21m;

    public static readonly MailingContact Treasurer = new("penningmeester@vrolijkedrammers.nl", "penningmeester");

    public sealed record InvoiceMail(Guid InvoiceId, bool Reminder = false);

    public async Task<InvoiceSettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        string[] keys =
        [
            AppConfigurationKeys.InvoiceAddress, AppConfigurationKeys.InvoiceKvk, AppConfigurationKeys.InvoiceVatNumber,
            AppConfigurationKeys.InvoicePhone, AppConfigurationKeys.InvoiceWebsite,
            AppConfigurationKeys.AdvertiserServiceFeeOn, AppConfigurationKeys.AdvertiserServiceFeeCents, AppConfigurationKeys.InvoicePaymentTermDays,
        ];
        var values = await db.AppConfiguration.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        return new InvoiceSettings(
            values.GetValueOrDefault(AppConfigurationKeys.InvoiceAddress),
            values.TryGetValue(AppConfigurationKeys.InvoiceKvk, out var kvk) ? kvk : DefaultKvk,
            values.GetValueOrDefault(AppConfigurationKeys.InvoiceVatNumber),
            values.GetValueOrDefault(AppConfigurationKeys.InvoicePhone),
            values.GetValueOrDefault(AppConfigurationKeys.InvoiceWebsite),
            values.GetValueOrDefault(AppConfigurationKeys.AdvertiserServiceFeeOn) == "true",
            int.TryParse(values.GetValueOrDefault(AppConfigurationKeys.AdvertiserServiceFeeCents), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fee) ? fee : 0,
            int.TryParse(values.GetValueOrDefault(AppConfigurationKeys.InvoicePaymentTermDays), NumberStyles.Integer, CultureInfo.InvariantCulture, out var term) && term > 0 ? term : 14);
    }

    public async Task SetSettingsAsync(InvoiceSettings input, CancellationToken cancellationToken)
    {
        var address = input.Address?.Trim();
        var kvk = input.Kvk?.Replace(" ", string.Empty, StringComparison.Ordinal);
        var vat = input.VatNumber?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var phone = input.Phone?.Trim();
        var website = input.Website?.Trim();
        if (address?.Length > 300)
        {
            throw new DomainException(ErrorCodes.Validation, "Het adres is hooguit 300 tekens.");
        }

        if (!string.IsNullOrEmpty(kvk) && (kvk.Length != 8 || !kvk.All(char.IsAsciiDigit)))
        {
            throw new DomainException(ErrorCodes.Validation, "Het KvK-nummer bestaat uit 8 cijfers.");
        }

        if (!string.IsNullOrEmpty(vat) && (vat.Length is < 12 or > 14 || !vat.StartsWith("NL", StringComparison.Ordinal)))
        {
            throw new DomainException(ErrorCodes.Validation, "Het btw-nummer begint met NL en is 12 tot 14 tekens (bijv. NL005844824B01).");
        }

        if (input.ServiceFeeCents is < 0 or > 100000)
        {
            throw new DomainException(ErrorCodes.Validation, "De servicekosten zijn hooguit € 1000,00.");
        }

        if (input.PaymentTermDays is < 1 or > 365)
        {
            throw new DomainException(ErrorCodes.Validation, "De betalingstermijn is tussen 1 en 365 dagen.");
        }

        var values = new Dictionary<string, string>
        {
            [AppConfigurationKeys.InvoiceAddress] = address ?? "",
            [AppConfigurationKeys.InvoiceKvk] = kvk ?? "",
            [AppConfigurationKeys.InvoiceVatNumber] = vat ?? "",
            [AppConfigurationKeys.InvoicePhone] = phone ?? "",
            [AppConfigurationKeys.InvoiceWebsite] = website ?? "",
            [AppConfigurationKeys.AdvertiserServiceFeeOn] = input.ServiceFeeOn ? "true" : "false",
            [AppConfigurationKeys.AdvertiserServiceFeeCents] = input.ServiceFeeCents.ToString(CultureInfo.InvariantCulture),
            [AppConfigurationKeys.InvoicePaymentTermDays] = input.PaymentTermDays.ToString(CultureInfo.InvariantCulture),
        };
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
        await audit.WriteAsync(new AuditEntry("config.invoice.changed", "AppConfiguration", "invoice", null,
            JsonSerializer.Serialize(values, JsonSerializerOptions.Web)), cancellationToken);
    }

    public async Task<InvoiceIssuer> IssuerAsync(CancellationToken cancellationToken)
    {
        var creditor = await collections.GetCreditorAsync(cancellationToken);
        var settings = await GetSettingsAsync(cancellationToken);
        var lines = (settings.Address ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new InvoiceIssuer(creditor.Name ?? "Carnavalsvereniging De Vrolijke Drammers", creditor.Iban, lines, settings.Kvk, Treasurer.Email,
            settings.VatNumber, settings.Phone, settings.Website);
    }

    /// <summary>Wie in het jaar een factuur krijgt (opgehaald, met een bedrag, niet gratis) en wat er al is gemaakt en verstuurd.</summary>
    public async Task<InvoiceOverview> OverviewAsync(int year, CancellationToken cancellationToken)
    {
        var due = await db.AdvertiserYears.AsNoTracking()
            .Where(y => y.Year == year && y.Status == AdvertiserYearStatus.Collected && !y.IsFree && y.Amount > 0)
            .Join(db.Advertisers.Where(a => a.Active), y => y.AdvertiserId, a => a.Id, (y, a) => new { a.Id, a.Number, a.CompanyName, y.Amount, a.Payment, a.Email })
            .ToListAsync(cancellationToken);
        var invoices = await db.AdvertiserInvoices.AsNoTracking().Where(i => i.Year == year).ToDictionaryAsync(i => i.AdvertiserId, cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // Vervallen = verstuurd, nog niet betaald of ingetrokken, en de uiterste betaaldatum is voorbij.
        bool Overdue(AdvertiserInvoice i) => i.SentAt is not null && i.Status is not (InvoiceStatus.Paid or InvoiceStatus.Cancelled) && today > i.DueDate;

        var rows = due.Select(d => invoices.GetValueOrDefault(d.Id) is { } i
                ? new InvoiceOverviewRow(d.Id, d.Number, i.CompanyName, i.Amount, i.Payment, i.Email, i.Id, i.Number, i.InvoiceDate, i.SentAt,
                    i.Status, i.PaymentMethod, i.TotalInclVat, i.MollieStatus, i.PaidAt, i.DueDate, Overdue(i))
                : new InvoiceOverviewRow(d.Id, d.Number, d.CompanyName, d.Amount!.Value, d.Payment, d.Email, null, null, null, null))
            .Concat(invoices.Values.Where(i => due.All(d => d.Id != i.AdvertiserId))
                .Select(i => new InvoiceOverviewRow(i.AdvertiserId, 0, i.CompanyName, i.Amount, i.Payment, i.Email, i.Id, i.Number, i.InvoiceDate, i.SentAt,
                    i.Status, i.PaymentMethod, i.TotalInclVat, i.MollieStatus, i.PaidAt, i.DueDate, Overdue(i))))
            .OrderBy(r => r.InvoiceNumber ?? "~").ThenBy(r => r.CompanyName).ToList();
        return new InvoiceOverview(year, rows.Count(r => r.InvoiceId is null), rows.Count(r => r.InvoiceId is not null && r.SentAt is null && r.Email is not null),
            rows.Count(r => r.Email is null), rows);
    }

    /// <summary>Maakt de facturen die nog ontbreken, met oplopende nummers; bestaande facturen blijven ongewijzigd.</summary>
    public async Task<int> CreateAsync(int year, DateOnly date, CancellationToken cancellationToken)
    {
        var overview = await OverviewAsync(year, cancellationToken);
        var missing = overview.Rows.Where(r => r.InvoiceId is null).Select(r => r.AdvertiserId).ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        var advertisers = await db.Advertisers.AsNoTracking().Include(a => a.Years.Where(y => y.Year == year)).Where(a => missing.Contains(a.Id))
            .OrderBy(a => a.Number).ToListAsync(cancellationToken);
        var sequence = await db.AdvertiserInvoices.Where(i => i.Year == year).MaxAsync(i => (int?)i.Sequence, cancellationToken) ?? 0;
        var settings = await GetSettingsAsync(cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        foreach (var a in advertisers)
        {
            db.AdvertiserInvoices.Add(BuildInvoice(a, a.Years.Single(), year, date, ++sequence, settings, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.invoices-created", "AdvertiserInvoice", year.ToString(CultureInfo.InvariantCulture), null,
            $"{advertisers.Count} facturen"), cancellationToken);
        return advertisers.Count;
    }

    /// <summary>Maakt de factuur voor één adverteerder (als die nog ontbreekt en de bijdrage in dit jaar is opgehaald).</summary>
    public async Task<bool> CreateOneAsync(Guid advertiserId, int year, DateOnly date, CancellationToken cancellationToken)
    {
        if (await db.AdvertiserInvoices.AnyAsync(i => i.AdvertiserId == advertiserId && i.Year == year, cancellationToken))
        {
            return false;
        }

        var advertiser = await db.Advertisers.AsNoTracking().Include(a => a.Years.Where(y => y.Year == year))
            .SingleOrDefaultAsync(a => a.Id == advertiserId && a.Active, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Adverteerder niet gevonden.", DomainErrorKind.NotFound);
        var row = advertiser.Years.SingleOrDefault();
        if (row is null || row.Status != AdvertiserYearStatus.Collected || row.IsFree || row.Amount is not > 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Voor deze adverteerder is er dit jaar geen opgehaalde bijdrage om te factureren.");
        }

        var sequence = (await db.AdvertiserInvoices.Where(i => i.Year == year).MaxAsync(i => (int?)i.Sequence, cancellationToken) ?? 0) + 1;
        var settings = await GetSettingsAsync(cancellationToken);
        db.AdvertiserInvoices.Add(BuildInvoice(advertiser, row, year, date, sequence, settings, clock.UtcNow.UtcDateTime));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.invoice-created", "AdvertiserInvoice", advertiserId.ToString(), null,
            $"ADV-{year}-{sequence.ToString("0000", CultureInfo.InvariantCulture)}"), cancellationToken);
        return true;
    }

    /// <summary>Bouwt één factuur: bedragen excl. btw, 21% op advertenties, 0% op giften, servicekosten (21%) alleen online.</summary>
    private AdvertiserInvoice BuildInvoice(Advertiser a, AdvertiserYear row, int year, DateOnly date, int sequence, InvoiceSettings settings, DateTime now)
    {
        var net = row.Amount!.Value;
        var vatRate = a.Kind == AdvertiserKind.Advertisement ? Vat21 : 0m;
        var vat = Math.Round(net * vatRate, 2, MidpointRounding.AwayFromZero);
        var method = a.Payment == AdvertiserPayment.Cash ? InvoicePaymentMethod.Cash : InvoicePaymentMethod.Mollie;
        var feeNet = settings.ServiceFeeOn && method == InvoicePaymentMethod.Mollie ? settings.ServiceFeeCents / 100m : 0m;
        var feeVat = Math.Round(feeNet * Vat21, 2, MidpointRounding.AwayFromZero);
        return new AdvertiserInvoice
        {
            Id = IdGenerator.NewId(),
            AdvertiserId = a.Id,
            Year = year,
            Sequence = sequence,
            Number = $"ADV-{year}-{sequence.ToString("0000", CultureInfo.InvariantCulture)}",
            InvoiceDate = date,
            DueDate = date.AddDays(settings.PaymentTermDays),
            Amount = net,
            VatRate = vatRate,
            VatAmount = vat,
            ServiceFeeNet = feeNet,
            ServiceFeeVat = feeVat,
            TotalInclVat = net + vat + feeNet + feeVat,
            Description = InvoicePdf.Description(a.Kind, year),
            Payment = a.Payment,
            PaymentMethod = method,
            Status = row.PaidAt is not null ? InvoiceStatus.Paid : InvoiceStatus.Concept,
            CompanyName = a.CompanyName,
            ContactName = a.ContactName,
            AddressLine = a.AddressLine,
            PostalCode = a.PostalCode,
            City = a.City,
            Email = a.Email,
            MandateReference = a.Payment == AdvertiserPayment.Mandate ? a.MandateReference : null,
            IbanLast4 = a.Payment == AdvertiserPayment.Mandate ? a.IbanLast4 : null,
            PaidAt = row.PaidAt,
            CreatedAt = now,
            CreatedBy = actor.UserId,
        };
    }

    /// <summary>Verstuurt de nog niet verzonden facturen met een e-mailadres, verdeeld over de tijd (zoals mailings).</summary>
    public async Task<int> SendAsync(int year, CancellationToken cancellationToken)
    {
        var ids = await db.AdvertiserInvoices.Where(i => i.Year == year && i.SentAt == null && i.Email != null).OrderBy(i => i.Sequence)
            .Select(i => i.Id).ToListAsync(cancellationToken);
        var interval = TimeSpan.FromHours(1) / mailingOptions.Value.EffectiveMaxPerHour;
        var now = clock.UtcNow.UtcDateTime;
        for (var i = 0; i < ids.Count; i++)
        {
            outbox.Enqueue(MailMessageType, new InvoiceMail(ids[i]), i == 0 ? null : now + (interval * i));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.invoices-sent", "AdvertiserInvoice", year.ToString(CultureInfo.InvariantCulture), null,
            $"{ids.Count} facturen"), cancellationToken);
        return ids.Count;
    }

    /// <summary>Verstuurt een herinnering (de factuur opnieuw, met herinneringstekst) naar de gekozen, nog niet betaalde facturen met e-mailadres.</summary>
    public async Task<int> RemindAsync(IReadOnlyList<Guid> invoiceIds, CancellationToken cancellationToken)
    {
        var ids = await db.AdvertiserInvoices
            .Where(i => invoiceIds.Contains(i.Id) && i.SentAt != null && i.Email != null
                && i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled)
            .OrderBy(i => i.Sequence).Select(i => i.Id).ToListAsync(cancellationToken);
        var interval = TimeSpan.FromHours(1) / mailingOptions.Value.EffectiveMaxPerHour;
        var now = clock.UtcNow.UtcDateTime;
        for (var i = 0; i < ids.Count; i++)
        {
            outbox.Enqueue(MailMessageType, new InvoiceMail(ids[i], Reminder: true), i == 0 ? null : now + (interval * i));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.invoices-reminded", "AdvertiserInvoice", "reminder", null, $"{ids.Count} herinneringen"), cancellationToken);
        return ids.Count;
    }

    public async Task<(string FileName, byte[] Content)> PdfAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.AdvertiserInvoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Factuur niet gevonden.", DomainErrorKind.NotFound);
        return ($"Factuur {invoice.Number}.pdf", InvoicePdf.Generate(invoice, await IssuerAsync(cancellationToken)));
    }

    /// <summary>Alle facturen van het jaar in één PDF; met <paramref name="withoutEmailOnly"/> alleen wie geen e-mailadres heeft (om te printen).</summary>
    public async Task<(string FileName, byte[] Content)> AllPdfAsync(int year, bool withoutEmailOnly, CancellationToken cancellationToken)
    {
        var invoices = await db.AdvertiserInvoices.AsNoTracking().Where(i => i.Year == year && (!withoutEmailOnly || i.Email == null))
            .OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        if (invoices.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Er zijn (nog) geen facturen om af te drukken.");
        }

        return ($"Facturen {year}{(withoutEmailOnly ? " zonder e-mail" : "")}.pdf", InvoicePdf.Generate(invoices, await IssuerAsync(cancellationToken)));
    }

    // ---- Online betalen via Mollie (fase 27o) --------------------------------------------------------------------

    /// <summary>De betaallink voor in de factuurmail; het token (DataProtector over het factuur-id) beveiligt de link.</summary>
    public string PayLink(string baseUrl, Guid invoiceId) =>
        $"{baseUrl}/api/v1/advertisers/invoices/{invoiceId}/pay?t={Uri.EscapeDataString(_tokens.Protect(invoiceId.ToString()))}";

    /// <summary>
    /// De betaallink uit de mail: staat de vorige Mollie-betaling nog open, dan dezelfde betaalpagina (geen tweede
    /// betaling); anders een nieuwe. Geeft de Mollie-checkout-URL om naar door te sturen.
    /// </summary>
    public async Task<string> PayAsync(Guid invoiceId, string token, string baseUrl, CancellationToken cancellationToken)
    {
        if (!TokenMatches(token, invoiceId))
        {
            throw new DomainException(ErrorCodes.NotFound, "Factuur niet gevonden.", DomainErrorKind.NotFound);
        }

        var invoice = await db.AdvertiserInvoices.SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Factuur niet gevonden.", DomainErrorKind.NotFound);
        if (invoice.PaymentMethod != InvoicePaymentMethod.Mollie)
        {
            throw new DomainException(ErrorCodes.OrderNotPayable, "Deze factuur kan niet online worden betaald.", DomainErrorKind.Conflict);
        }

        var existing = invoice.MolliePaymentId is { } previous ? await SyncAsync(invoice, previous, cancellationToken) : null;
        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw new DomainException(ErrorCodes.OrderAlreadyPaid, "Deze factuur is al betaald.", DomainErrorKind.Conflict);
        }

        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            throw new DomainException(ErrorCodes.OrderNotPayable, "Deze betaallink is niet meer geldig.", DomainErrorKind.Conflict);
        }

        if (existing is { Status: "open", CheckoutUrl: { } checkout })
        {
            return checkout;
        }

        var now = clock.UtcNow.UtcDateTime;
        try
        {
            var created = await mollie.CreatePaymentAsync(new MollieNewPayment(
                (int)Math.Round(invoice.TotalInclVat * 100m, MidpointRounding.AwayFromZero), $"Factuur {invoice.Number}", $"{baseUrl}/",
                WebhookUrl(baseUrl), invoice.Id, $"{invoice.Id:N}-{now.Ticks}"), cancellationToken);
            invoice.MolliePaymentId = created.Id;
            invoice.MollieStatus = created.Status;
            db.AdvertiserInvoiceMolliePayments.Add(new AdvertiserInvoiceMolliePayment
            {
                Id = IdGenerator.NewId(),
                InvoiceId = invoice.Id,
                MolliePaymentId = created.Id,
                MollieStatus = created.Status,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            return created.CheckoutUrl ?? throw new MollieException("Geen betaalpagina van Mollie.");
        }
        catch (Exception ex) when (ex is MollieException or System.Net.Http.HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Mollie-betaling aanmaken mislukt voor factuur {Number}.", invoice.Number);
            throw new DomainException(ErrorCodes.PaymentFailed, "Betalen lukt nu niet. Probeer het over een paar minuten opnieuw.", DomainErrorKind.Conflict);
        }
    }

    /// <summary>Webhook van Mollie; geeft <c>false</c> als de betaling niet bij een factuur hoort.</summary>
    public async Task<bool> HandleWebhookAsync(string molliePaymentId, CancellationToken cancellationToken)
    {
        var invoiceId = await db.AdvertiserInvoiceMolliePayments.Where(p => p.MolliePaymentId == molliePaymentId)
            .Select(p => (Guid?)p.InvoiceId).SingleOrDefaultAsync(cancellationToken);
        var invoice = invoiceId is null ? null : await db.AdvertiserInvoices.SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return false;
        }

        await SyncAsync(invoice, molliePaymentId, cancellationToken);
        return true;
    }

    /// <summary>Status bij Mollie opnieuw ophalen (de webhook doet dit ook vanzelf).</summary>
    public async Task RefreshPaymentAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await db.AdvertiserInvoices.SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Factuur niet gevonden.", DomainErrorKind.NotFound);
        if (invoice.MolliePaymentId is { } molliePaymentId)
        {
            await SyncAsync(invoice, molliePaymentId, cancellationToken);
        }
    }

    /// <summary>Een factuur intrekken (bijv. verkeerd bedrag): de betaallink werkt dan niet meer.</summary>
    public async Task CancelAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await db.AdvertiserInvoices.SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Factuur niet gevonden.", DomainErrorKind.NotFound);
        if (invoice.MolliePaymentId is { } molliePaymentId)
        {
            await SyncAsync(invoice, molliePaymentId, cancellationToken);
        }

        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw new DomainException(ErrorCodes.OrderAlreadyPaid, "Een betaalde factuur kan niet worden ingetrokken.", DomainErrorKind.Conflict);
        }

        invoice.Status = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.invoice-cancelled", "AdvertiserInvoice", invoice.Id.ToString(), null, invoice.Number), cancellationToken);
    }

    private bool TokenMatches(string token, Guid invoiceId)
    {
        try
        {
            return Guid.TryParse(_tokens.Unprotect(token), out var id) && id == invoiceId;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Haalt een Mollie-betaling op en werkt de factuur bij (idempotent). Een tweede betaalde Mollie-betaling voor
    /// dezelfde factuur wordt niet nog eens geboekt, maar gemarkeerd om terug te betalen.
    /// </summary>
    private async Task<MolliePayment?> SyncAsync(AdvertiserInvoice invoice, string molliePaymentId, CancellationToken cancellationToken)
    {
        var mollieResult = await mollie.GetPaymentAsync(molliePaymentId, cancellationToken);
        if (mollieResult.OrderId != invoice.Id)
        {
            return null;
        }

        var attempt = await db.AdvertiserInvoiceMolliePayments.SingleOrDefaultAsync(m => m.MolliePaymentId == molliePaymentId, cancellationToken);
        if (attempt is not null)
        {
            attempt.MollieStatus = mollieResult.Status;
        }

        if (invoice.MolliePaymentId == molliePaymentId || mollieResult.IsPaid)
        {
            invoice.MollieStatus = mollieResult.Status;
        }

        var now = clock.UtcNow.UtcDateTime;
        var expectedCents = (int)Math.Round(invoice.TotalInclVat * 100m, MidpointRounding.AwayFromZero);
        if (mollieResult.IsPaid && attempt?.PaidAt is null && mollieResult.AmountCents == expectedCents)
        {
            if (attempt is not null)
            {
                attempt.PaidAt = mollieResult.PaidAt ?? now;
            }

            if (invoice.Status == InvoiceStatus.Paid && attempt is not null)
            {
                attempt.DuplicatePaid = true;
                await db.SaveChangesAsync(cancellationToken);
                await audit.WriteAsync(new AuditEntry("advertiser.invoice-paid-twice", "AdvertiserInvoice", invoice.Id.ToString(), null, molliePaymentId), cancellationToken);
                return mollieResult;
            }

            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAt = mollieResult.PaidAt ?? now;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("advertiser.invoice-paid", "AdvertiserInvoice", invoice.Id.ToString(), null, molliePaymentId), cancellationToken);
            return mollieResult;
        }

        await db.SaveChangesAsync(cancellationToken);
        return mollieResult;
    }

    private static string? WebhookUrl(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && !uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttps
            ? $"{baseUrl}/api/v1/payments/mollie/webhook"
            : null;
}

/// <summary>Verstuurt één factuur als PDF per e-mail namens de penningmeester (outbox).</summary>
public sealed class AdvertiserInvoiceMailHandler(
    DrammersDbContext db, AdvertiserInvoices invoices, IEmailSender email, IClock clock, IOptions<EmailOptions> emailOptions, IOptions<MailingOptions> mailingOptions)
    : IOutboxMessageHandler
{
    public string Type => AdvertiserInvoices.MailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var mail = JsonSerializer.Deserialize<AdvertiserInvoices.InvoiceMail>(message.Payload, JsonSerializerOptions.Web)!;
        var invoice = await db.AdvertiserInvoices.SingleOrDefaultAsync(i => i.Id == mail.InvoiceId, cancellationToken);
        if (invoice is null || invoice.Email is null)
        {
            return;
        }

        // De eerste verzending slaat een al verstuurde factuur over; een herinnering juist alleen als de factuur verstuurd
        // is en nog niet betaald/ingetrokken.
        if (mail.Reminder
            ? invoice.SentAt is null || invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled
            : invoice.SentAt is not null)
        {
            return;
        }

        var issuer = await invoices.IssuerAsync(cancellationToken);
        var pdf = InvoicePdf.Generate(invoice, issuer);
        var person = new MailingPerson(invoice.ContactName is null ? invoice.CompanyName : null, invoice.ContactName ?? invoice.CompanyName, invoice.CompanyName);
        var baseUrl = mailingOptions.Value.PublicBaseUrl;
        var payLink = baseUrl is not null && invoice.PaymentMethod == InvoicePaymentMethod.Mollie && invoice.Status != InvoiceStatus.Paid
            ? invoices.PayLink(baseUrl, invoice.Id)
            : null;
        var intro = mail.Reminder
            ? $"Beste {{voornaam}},\n\nEen vriendelijke herinnering: de factuur voor {{bedrijf}} ({invoice.Description}) staat nog open. In de bijlage vindt u de factuur opnieuw."
            : $"Beste {{voornaam}},\n\nHartelijk dank voor de steun van {{bedrijf}} aan De Vrolijke Drammers. In de bijlage vindt u de factuur voor: {invoice.Description}.";
        var blocks = new List<MailingBlock>
        {
            new("heading", mail.Reminder ? $"Herinnering factuur {invoice.Number}" : $"Factuur {invoice.Number}"),
            new("text", intro),
            new("highlight", InvoicePdf.Money(invoice.TotalInclVat), "Bedrag", Note: InvoicePdf.PaymentText(invoice, issuer)),
        };

        // Een betaalknop alleen bij een echte https-link (niet in dev/tests); anders staat het overmaken al in de bedrag-notitie.
        if (payLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out var linkUri) && linkUri.Scheme == Uri.UriSchemeHttps)
        {
            blocks.Add(new MailingBlock("button", Label: "Betaal met iDEAL", Url: link, Align: "center"));
        }

        blocks.Add(new MailingBlock("closing", "Met vriendelijke groet,\nPenningmeester De Vrolijke Drammers"));
        var subject = mail.Reminder ? $"Herinnering: factuur {invoice.Number} – {invoice.Description}" : $"Factuur {invoice.Number} – {invoice.Description}";
        var rendered = MailingRenderer.Render(subject, null, blocks, person, emailOptions.Value.LogoUrl, _ => null, null, baseUrl, AdvertiserInvoices.Treasurer);
        await email.SendAsync(new EmailMessage(invoice.Email, rendered.Subject, rendered.PlainText, rendered.Html, AdvertiserInvoices.Treasurer.Email,
            AdvertiserInvoices.Treasurer.Role, [new EmailFile($"Factuur {invoice.Number}.pdf", "application/pdf", pdf)]), cancellationToken);
        if (!mail.Reminder)
        {
            invoice.SentAt = clock.UtcNow.UtcDateTime;
            if (invoice.Status == InvoiceStatus.Concept)
            {
                invoice.Status = InvoiceStatus.Sent;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
