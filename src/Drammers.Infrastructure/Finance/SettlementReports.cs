using System.Globalization;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Advertisers.Invoices;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Exports;
using Drammers.Infrastructure.Mailings;
using Drammers.Infrastructure.Payments;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Messaging;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Finance;

/// <summary>Eén Mollie-afrekening (bankuitbetaling): omzet min Mollie-kosten = wat op de bank is bijgeschreven.</summary>
public sealed record SettlementSummary(string Id, string? Reference, string Status, DateOnly? Date, decimal Gross, decimal Costs, decimal Net);

/// <summary>Eén inkomst binnen een afrekening, herleid naar onze eigen administratie (factuur, festival of kaartverkoop).</summary>
public sealed record SettlementLine(string Source, string Number, string Party, decimal Amount, string MollieId);

/// <summary>Een afrekening met de herleide inkomsten en het deel dat niet aan een eigen post gekoppeld kon worden.</summary>
public sealed record SettlementDetail(SettlementSummary Settlement, decimal MatchedTotal, int UnmatchedCount, decimal UnmatchedTotal, IReadOnlyList<SettlementLine> Lines);

/// <summary>
/// Mollie-afrekeningen/verzamelstaat (fase 4b): de uitbetalingen van Mollie naar de bankrekening, met per uitbetaling de
/// omzet, de Mollie-kosten en het netto bedrag (= de bankregel), plus welke eigen inkomsten erin zitten (factuur ADV-…,
/// festival FEST-… of kaartverkoop). Een CSV-verzamelstaat en een verstuur-knop (namens de penningmeester) voor de boekhouding.
/// </summary>
public sealed class MollieSettlements(IMollieClient mollie, DrammersDbContext db, IOutbox outbox)
{
    public const string MailMessageType = "finance.settlement-mail";

    /// <summary>De verzamelstaat per e-mail versturen (namens de penningmeester), met de CSV als bijlage.</summary>
    public sealed record SettlementMail(string SettlementId, string Recipient);

    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public async Task<IReadOnlyList<SettlementSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var settlements = await mollie.ListSettlementsAsync(cancellationToken);
        return [.. settlements.Select(Summary).OrderByDescending(s => s.Date)];
    }

    public async Task<SettlementDetail> DetailAsync(string settlementId, CancellationToken cancellationToken)
    {
        var settlement = await mollie.GetSettlementAsync(settlementId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "De afrekening bestaat niet.", DomainErrorKind.NotFound);
        var payments = await mollie.ListSettlementPaymentsAsync(settlementId, cancellationToken);
        var ids = payments.Select(p => p.Id).ToList();

        var adv = await db.AdvertiserInvoiceMolliePayments.AsNoTracking().Where(m => ids.Contains(m.MolliePaymentId))
            .Join(db.AdvertiserInvoices.AsNoTracking(), m => m.InvoiceId, i => i.Id, (m, i) => new { m.MolliePaymentId, i.Number, i.CompanyName })
            .ToDictionaryAsync(x => x.MolliePaymentId, cancellationToken);
        var fest = await db.DanceFestivalMolliePayments.AsNoTracking().Where(m => ids.Contains(m.MolliePaymentId))
            .Join(db.DanceFestivalPayments.AsNoTracking(), m => m.PaymentId, p => p.Id, (m, p) => new { m.MolliePaymentId, p.InvoiceNumber, p.RegistrationId })
            .Join(db.DanceFestivalRegistrations.AsNoTracking(), x => x.RegistrationId, r => r.Id, (x, r) => new { x.MolliePaymentId, x.InvoiceNumber, r.ClubName })
            .ToDictionaryAsync(x => x.MolliePaymentId, cancellationToken);
        var sale = await db.SaleOrderPayments.AsNoTracking().Where(m => ids.Contains(m.MolliePaymentId))
            .Join(db.SaleOrders.AsNoTracking(), m => m.OrderId, o => o.Id, (m, o) => new { m.MolliePaymentId, o.Number })
            .ToDictionaryAsync(x => x.MolliePaymentId, cancellationToken);

        var lines = new List<SettlementLine>();
        var unmatchedCount = 0;
        var unmatchedTotal = 0m;
        foreach (var p in payments)
        {
            var amount = p.AmountCents / 100m;
            if (adv.TryGetValue(p.Id, out var a))
            {
                lines.Add(new SettlementLine("Advertentie", a.Number, a.CompanyName, amount, p.Id));
            }
            else if (fest.TryGetValue(p.Id, out var f))
            {
                lines.Add(new SettlementLine("Festival", f.InvoiceNumber ?? "", f.ClubName, amount, p.Id));
            }
            else if (sale.TryGetValue(p.Id, out var s))
            {
                lines.Add(new SettlementLine("Kaartverkoop", s.Number, "", amount, p.Id));
            }
            else
            {
                // Terugbetalingen en betalingen buiten onze administratie (bv. handmatig in Mollie) kunnen we niet herleiden.
                unmatchedCount++;
                unmatchedTotal += amount;
            }
        }

        var ordered = lines.OrderBy(l => l.Source).ThenBy(l => l.Number, StringComparer.Ordinal).ToList();
        return new SettlementDetail(Summary(settlement), ordered.Sum(l => l.Amount), unmatchedCount, unmatchedTotal, ordered);
    }

    public async Task<(string FileName, byte[] Content)> ExportCsvAsync(string settlementId, CancellationToken cancellationToken)
    {
        var detail = await DetailAsync(settlementId, cancellationToken);
        var s = detail.Settlement;
        var csv = new StringBuilder();
        csv.AppendLine(Csv.Row(["Afrekening", s.Reference ?? s.Id]));
        csv.AppendLine(Csv.Row(["Datum", s.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""]));
        csv.AppendLine(Csv.Row(["Omzet (bruto)", Euro(s.Gross)]));
        csv.AppendLine(Csv.Row(["Mollie-kosten", Euro(s.Costs)]));
        csv.AppendLine(Csv.Row(["Uitbetaald (netto)", Euro(s.Net)]));
        csv.AppendLine();
        csv.AppendLine("Bron;Nummer;Partij;Bedrag;Mollie-id");
        foreach (var l in detail.Lines)
        {
            csv.AppendLine(Csv.Row([l.Source, l.Number, l.Party, Euro(l.Amount), l.MollieId]));
        }

        if (detail.UnmatchedCount > 0)
        {
            csv.AppendLine(Csv.Row(["Niet herleid", $"{detail.UnmatchedCount} betaling(en)", "", Euro(detail.UnmatchedTotal), ""]));
        }

        var bytes = new List<byte>(Encoding.UTF8.GetPreamble());
        bytes.AddRange(Encoding.UTF8.GetBytes(csv.ToString()));
        return ($"Afrekening {s.Reference ?? s.Id}.csv", [.. bytes]);
    }

    public async Task SendAsync(string settlementId, string recipient, CancellationToken cancellationToken)
    {
        var to = recipient.Trim();
        if (!MailAddressIsValid(to))
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een geldig e-mailadres in.", DomainErrorKind.Validation);
        }

        // Controleer dat de afrekening bestaat voordat we de mail inplannen.
        _ = await mollie.GetSettlementAsync(settlementId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "De afrekening bestaat niet.", DomainErrorKind.NotFound);
        outbox.Enqueue(MailMessageType, new SettlementMail(settlementId, to));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static SettlementSummary Summary(MollieSettlement s) => new(
        s.Id, s.Reference, StatusLabel(s.Status), s.SettledAt is { } settled ? DateOnly.FromDateTime(settled) : s.CreatedAt is { } created ? DateOnly.FromDateTime(created) : null,
        s.GrossCents / 100m, s.CostsCents / 100m, s.NetCents / 100m);

    private static string StatusLabel(string status) => status switch
    {
        "paidout" => "Uitbetaald",
        "pending" => "In behandeling",
        "open" => "Open",
        "failed" => "Mislukt",
        _ => status,
    };

    private static string Euro(decimal value) => value.ToString("0.00", Nl);

    private static bool MailAddressIsValid(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at < value.Length - 1 && !value.Contains(' ', StringComparison.Ordinal) && value.LastIndexOf('.') > at;
    }
}

/// <summary>Verstuurt de verzamelstaat van een afrekening als CSV-bijlage, namens de penningmeester.</summary>
public sealed class SettlementMailHandler(
    MollieSettlements settlements, IEmailSender email, IOptions<EmailOptions> emailOptions, IOptions<MailingOptions> mailingOptions) : IOutboxMessageHandler
{
    public string Type => MollieSettlements.MailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var mail = JsonSerializer.Deserialize<MollieSettlements.SettlementMail>(message.Payload, JsonSerializerOptions.Web)!;
        var detail = await settlements.DetailAsync(mail.SettlementId, cancellationToken);
        var (fileName, csv) = await settlements.ExportCsvAsync(mail.SettlementId, cancellationToken);
        var s = detail.Settlement;
        var naam = s.Reference ?? s.Id;
        var blocks = new List<MailingBlock>
        {
            new("heading", $"Verzamelstaat afrekening {naam}"),
            new("text", $"Beste penningmeester,\n\nHierbij de verzamelstaat van Mollie-afrekening {naam}"
                + (s.Date is { } d ? $" van {d.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("nl-NL"))}" : "") + ". De CSV in de bijlage is in te lezen in de boekhouding."),
            new("highlight", s.Net.ToString("C", CultureInfo.GetCultureInfo("nl-NL")), "Uitbetaald (netto)",
                Note: $"Omzet {s.Gross.ToString("C", CultureInfo.GetCultureInfo("nl-NL"))} − Mollie-kosten {s.Costs.ToString("C", CultureInfo.GetCultureInfo("nl-NL"))}"),
            new("closing", "Met vriendelijke groet,\nPenningmeester De Vrolijke Drammers"),
        };

        var person = new MailingPerson(null, "penningmeester");
        var rendered = MailingRenderer.Render($"Verzamelstaat afrekening {naam}", null, blocks, person, emailOptions.Value.LogoUrl, _ => null, null,
            mailingOptions.Value.PublicBaseUrl, AdvertiserInvoices.Treasurer);
        await email.SendAsync(new EmailMessage(mail.Recipient, rendered.Subject, rendered.PlainText, rendered.Html, AdvertiserInvoices.Treasurer.Email,
            AdvertiserInvoices.Treasurer.Role, [new EmailFile(fileName, "text/csv", csv)]), cancellationToken);
    }
}
