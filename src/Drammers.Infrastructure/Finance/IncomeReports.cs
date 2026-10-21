using System.Globalization;
using System.Text;
using Drammers.Infrastructure.Exports;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Festival;
using Drammers.Modules.Membership.Advertisers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Finance;

/// <summary>Waar de inkomst vandaan komt.</summary>
public enum IncomeSource
{
    Advertentie,
    Festival,
}

/// <summary>Eén inkomstregel (een adverteerders-factuur of een festival-betaalverzoek), bedragen in euro's.</summary>
public sealed record IncomeRow(
    IncomeSource Source, string Number, string Party, DateOnly Date, decimal NetAmount, decimal VatAmount, decimal ServiceFeeAmount,
    decimal TotalAmount, string PaymentMethod, string Status, DateOnly? PaidAt, string? MollieId);

public sealed record IncomeTotals(decimal Net, decimal Vat, decimal ServiceFee, decimal Total, decimal Paid);

public sealed record IncomeReport(int Year, IncomeTotals Totals, IReadOnlyList<IncomeRow> Rows);

/// <summary>
/// Gezamenlijk inkomstenoverzicht (fase 4a): adverteerders-facturen en festival-betaalverzoeken van een campagnejaar in
/// één lijst, met de btw-uitsplitsing, de stand en de Mollie-referentie — als bron om in SnelStart over te nemen.
/// </summary>
public sealed class IncomeReports(DrammersDbContext db, IClock clock)
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public async Task<IncomeReport> ReportAsync(int year, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var invoices = await db.AdvertiserInvoices.AsNoTracking().Where(i => i.Year == year).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        var advertiserRows = invoices.Select(i => new IncomeRow(
            IncomeSource.Advertentie, i.Number, i.CompanyName, i.InvoiceDate, i.Amount, i.VatAmount, i.ServiceFeeNet + i.ServiceFeeVat,
            i.TotalInclVat, i.PaymentMethod == InvoicePaymentMethod.Cash ? "Contant" : "iDEAL (Mollie)", AdvertiserStatus(i, today),
            i.PaidAt is { } paid ? DateOnly.FromDateTime(paid) : null, i.MolliePaymentId));

        var prefix = $"FEST-{year.ToString(CultureInfo.InvariantCulture)}-";
        var payments = await db.DanceFestivalPayments.AsNoTracking().Where(p => p.InvoiceNumber != null && p.InvoiceNumber.StartsWith(prefix))
            .Join(db.DanceFestivalRegistrations.AsNoTracking(), p => p.RegistrationId, r => r.Id, (p, r) => new { p, r.ClubName })
            .OrderBy(x => x.p.InvoiceNumber).ToListAsync(cancellationToken);
        var festivalRows = payments.Select(x => new IncomeRow(
            IncomeSource.Festival, x.p.InvoiceNumber!, x.ClubName, DateOnly.FromDateTime(x.p.CreatedAt), x.p.NetCents / 100m, x.p.VatCents / 100m,
            (x.p.ServiceFeeNetCents + x.p.ServiceFeeVatCents) / 100m, x.p.AmountCents / 100m, "iDEAL (Mollie)", FestivalStatus(x.p.Status),
            x.p.PaidAt is { } paid ? DateOnly.FromDateTime(paid) : null, x.p.MolliePaymentId));

        var rows = advertiserRows.Concat(festivalRows).OrderBy(r => r.Source).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
        var totals = new IncomeTotals(
            rows.Sum(r => r.NetAmount), rows.Sum(r => r.VatAmount), rows.Sum(r => r.ServiceFeeAmount), rows.Sum(r => r.TotalAmount),
            rows.Where(r => r.Status == "Betaald").Sum(r => r.TotalAmount));
        return new IncomeReport(year, totals, rows);
    }

    public async Task<byte[]> ExportCsvAsync(int year, CancellationToken cancellationToken)
    {
        var report = await ReportAsync(year, cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine("Bron;Nummer;Partij;Datum;Bedrag excl. btw;Btw;Servicekosten;Totaal incl. btw;Betaalwijze;Status;Betaald op;Mollie-id");
        foreach (var r in report.Rows)
        {
            csv.AppendLine(Csv.Row(
            [
                r.Source.ToString(), r.Number, r.Party, r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Euro(r.NetAmount), Euro(r.VatAmount),
                Euro(r.ServiceFeeAmount), Euro(r.TotalAmount), r.PaymentMethod, r.Status,
                r.PaidAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", r.MollieId ?? "",
            ]));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Euro(decimal value) => value.ToString("0.00", Nl);

    private static string AdvertiserStatus(AdvertiserInvoice invoice, DateOnly today) => invoice.Status switch
    {
        InvoiceStatus.Paid => "Betaald",
        InvoiceStatus.Cancelled => "Ingetrokken",
        _ when invoice.SentAt is not null && today > invoice.DueDate => "Vervallen",
        InvoiceStatus.Sent => "Verstuurd",
        _ => "Concept",
    };

    private static string FestivalStatus(DanceFestivalPaymentStatus status) => status switch
    {
        DanceFestivalPaymentStatus.Paid => "Betaald",
        DanceFestivalPaymentStatus.Canceled => "Ingetrokken",
        _ => "Open",
    };
}
