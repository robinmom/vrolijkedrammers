using System.Globalization;
using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Mailings;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Advertisers;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Advertisers.Invoices;

public sealed record InvoiceSettings(string? Address, string? Kvk);

/// <summary>Een adverteerder in het factuuroverzicht van een jaar, met of zonder factuur.</summary>
public sealed record InvoiceOverviewRow(
    Guid AdvertiserId, int AdvertiserNumber, string CompanyName, decimal Amount, AdvertiserPayment Payment, string? Email, Guid? InvoiceId,
    string? InvoiceNumber, DateOnly? InvoiceDate, DateTime? SentAt);

public sealed record InvoiceOverview(int Year, int ToCreate, int ToSend, int WithoutEmail, IReadOnlyList<InvoiceOverviewRow> Rows);

/// <summary>
/// Facturen aan adverteerders (fase 27e): per campagnejaar één factuur voor elke opgehaalde bijdrage (niet gratis), met
/// een vast nummer ADV-jaar-volgnummer. Versturen als PDF per e-mail namens de penningmeester; wie geen e-mailadres heeft,
/// krijgt een PDF om te printen.
/// </summary>
public sealed class AdvertiserInvoices(
    DrammersDbContext db, SepaCollections collections, IOutbox outbox, IAuditLogger audit, IClock clock, ICurrentActor actor,
    IOptions<MailingOptions> mailingOptions)
{
    public const string MailMessageType = "advertiser.invoice-mail";

    /// <summary>Het KvK-nummer uit de oude conceptfactuur, als er nog niets is ingesteld.</summary>
    public const string DefaultKvk = "40122564";

    public static readonly MailingContact Treasurer = new("penningmeester@vrolijkedrammers.nl", "penningmeester");

    public sealed record InvoiceMail(Guid InvoiceId);

    public async Task<InvoiceSettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        string[] keys = [AppConfigurationKeys.InvoiceAddress, AppConfigurationKeys.InvoiceKvk];
        var values = await db.AppConfiguration.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        return new InvoiceSettings(values.GetValueOrDefault(AppConfigurationKeys.InvoiceAddress),
            values.TryGetValue(AppConfigurationKeys.InvoiceKvk, out var kvk) ? kvk : DefaultKvk);
    }

    public async Task SetSettingsAsync(InvoiceSettings input, CancellationToken cancellationToken)
    {
        var address = input.Address?.Trim();
        var kvk = input.Kvk?.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (address?.Length > 300)
        {
            throw new DomainException(ErrorCodes.Validation, "Het adres is hooguit 300 tekens.");
        }

        if (!string.IsNullOrEmpty(kvk) && (kvk.Length != 8 || !kvk.All(char.IsAsciiDigit)))
        {
            throw new DomainException(ErrorCodes.Validation, "Het KvK-nummer bestaat uit 8 cijfers.");
        }

        var values = new Dictionary<string, string> { [AppConfigurationKeys.InvoiceAddress] = address ?? "", [AppConfigurationKeys.InvoiceKvk] = kvk ?? "" };
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
        return new InvoiceIssuer(creditor.Name ?? "Carnavalsvereniging De Vrolijke Drammers", creditor.Iban, lines, settings.Kvk, Treasurer.Email);
    }

    /// <summary>Wie in het jaar een factuur krijgt (opgehaald, met een bedrag, niet gratis) en wat er al is gemaakt en verstuurd.</summary>
    public async Task<InvoiceOverview> OverviewAsync(int year, CancellationToken cancellationToken)
    {
        var due = await db.AdvertiserYears.AsNoTracking()
            .Where(y => y.Year == year && y.Status == AdvertiserYearStatus.Collected && !y.IsFree && y.Amount > 0)
            .Join(db.Advertisers.Where(a => a.Active), y => y.AdvertiserId, a => a.Id, (y, a) => new { a.Id, a.Number, a.CompanyName, y.Amount, a.Payment, a.Email })
            .ToListAsync(cancellationToken);
        var invoices = await db.AdvertiserInvoices.AsNoTracking().Where(i => i.Year == year).ToDictionaryAsync(i => i.AdvertiserId, cancellationToken);

        var rows = due.Select(d => invoices.GetValueOrDefault(d.Id) is { } i
                ? new InvoiceOverviewRow(d.Id, d.Number, i.CompanyName, i.Amount, i.Payment, i.Email, i.Id, i.Number, i.InvoiceDate, i.SentAt)
                : new InvoiceOverviewRow(d.Id, d.Number, d.CompanyName, d.Amount!.Value, d.Payment, d.Email, null, null, null, null))
            .Concat(invoices.Values.Where(i => due.All(d => d.Id != i.AdvertiserId))
                .Select(i => new InvoiceOverviewRow(i.AdvertiserId, 0, i.CompanyName, i.Amount, i.Payment, i.Email, i.Id, i.Number, i.InvoiceDate, i.SentAt)))
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
        var now = clock.UtcNow.UtcDateTime;
        foreach (var a in advertisers)
        {
            var row = a.Years.Single();
            sequence++;
            db.AdvertiserInvoices.Add(new AdvertiserInvoice
            {
                Id = IdGenerator.NewId(),
                AdvertiserId = a.Id,
                Year = year,
                Sequence = sequence,
                Number = $"ADV-{year}-{sequence.ToString("0000", CultureInfo.InvariantCulture)}",
                InvoiceDate = date,
                Amount = row.Amount!.Value,
                Description = InvoicePdf.Description(a.Kind, year),
                Payment = a.Payment,
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
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.invoices-created", "AdvertiserInvoice", year.ToString(CultureInfo.InvariantCulture), null,
            $"{advertisers.Count} facturen"), cancellationToken);
        return advertisers.Count;
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
}

/// <summary>Verstuurt één factuur als PDF per e-mail namens de penningmeester (outbox).</summary>
public sealed class AdvertiserInvoiceMailHandler(
    DrammersDbContext db, AdvertiserInvoices invoices, IEmailSender email, IClock clock, IOptions<EmailOptions> emailOptions, IOptions<MailingOptions> mailingOptions)
    : IOutboxMessageHandler
{
    public string Type => AdvertiserInvoices.MailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var id = JsonSerializer.Deserialize<AdvertiserInvoices.InvoiceMail>(message.Payload, JsonSerializerOptions.Web)!.InvoiceId;
        var invoice = await db.AdvertiserInvoices.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null || invoice.SentAt is not null || invoice.Email is null)
        {
            return;
        }

        var issuer = await invoices.IssuerAsync(cancellationToken);
        var pdf = InvoicePdf.Generate(invoice, issuer);
        var person = new MailingPerson(invoice.ContactName is null ? invoice.CompanyName : null, invoice.ContactName ?? invoice.CompanyName, invoice.CompanyName);
        var rendered = MailingRenderer.Render($"Factuur {invoice.Number} – {invoice.Description}", null,
            [
                new MailingBlock("heading", $"Factuur {invoice.Number}"),
                new MailingBlock("text", $"Beste {{voornaam}},\n\nHartelijk dank voor de steun van {{bedrijf}} aan De Vrolijke Drammers. In de bijlage vindt u de factuur voor: {invoice.Description}."),
                new MailingBlock("highlight", InvoicePdf.Money(invoice.Amount), "Bedrag", Note: InvoicePdf.PaymentText(invoice, issuer)),
                new MailingBlock("closing", "Met vriendelijke groet,\nPenningmeester De Vrolijke Drammers"),
            ],
            person, emailOptions.Value.LogoUrl, _ => null, null, mailingOptions.Value.PublicBaseUrl, AdvertiserInvoices.Treasurer);
        await email.SendAsync(new EmailMessage(invoice.Email, rendered.Subject, rendered.PlainText, rendered.Html, AdvertiserInvoices.Treasurer.Email,
            AdvertiserInvoices.Treasurer.Role, [new EmailFile($"Factuur {invoice.Number}.pdf", "application/pdf", pdf)]), cancellationToken);
        invoice.SentAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }
}
