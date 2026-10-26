using System.Globalization;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Content.Festival;
using Drammers.Modules.Membership.Advertisers;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Sales;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Finance;

/// <summary>Eén inkomstenbron met het verwachte totaal en wat daarvan betaald is (euro's, incl. btw).</summary>
public sealed record FinanceSourceAmount(string Source, decimal Total, decimal Paid);

/// <summary>Totaaloverzicht van een carnavalsjaar: de inkomstenbronnen (adverteerders, festival, kaartverkoop) met hun stand.</summary>
public sealed record FinanceOverviewReport(
    int CarnivalYearId, string Year, decimal Total, decimal Paid, decimal Open, IReadOnlyList<FinanceSourceAmount> Sources);

/// <summary>Het totaal van één carnavalsjaar (alle bronnen incl. contributie), voor de grafiek "per carnavalsjaar".</summary>
public sealed record FinanceYearTotal(int CarnivalYearId, string Year, decimal Total);

/// <summary>Statistieken: alles (incl. contributie) voor het gekozen jaar, de groei t.o.v. vorig jaar en de reeks per carnavalsjaar.</summary>
public sealed record FinanceStatisticsReport(
    int CarnivalYearId, string Year, decimal Total, decimal Paid, decimal Open, decimal? GrowthPercent,
    IReadOnlyList<FinanceSourceAmount> Sources, IReadOnlyList<FinanceYearTotal> PerYear);

/// <summary>Eén festival-factuur (betaalverzoek) voor de facturen-pagina, bedragen in euro's.</summary>
public sealed record FestivalInvoiceRow(string Number, string Club, DateOnly Date, decimal Total, string Status);

/// <summary>
/// Financieel (fase 4c): het totaaloverzicht en de statistieken per carnavalsjaar over de inkomstenbronnen —
/// adverteerders, Dansgardefestival en kaartverkoop (munten, pronkzitting, dagkaarten, activiteiten), en voor de
/// statistieken ook de contributie. Alles wordt op het carnavalsjaar herleid: het eindjaar van de editie (2026/2027 → 2027),
/// waarmee de adverteerders-facturen (kalenderjaar), de FEST-nummers en de verkoop (op CarnivalYearId) op één noemer komen.
/// </summary>
public sealed class FinanceReports(DrammersDbContext db)
{
    public const string Advertisers = "Adverteerders";
    public const string Festival = "Dansgardefestival";
    public const string Pronkzitting = "Pronkzitting";
    public const string DayTickets = "Dagkaarten";
    public const string EventTickets = "Activiteiten";
    public const string Tokens = "Munten";
    public const string Contribution = "Contributie";

    /// <summary>Totaaloverzicht: adverteerders + festival + kaartverkoop van het (gekozen of actieve) carnavalsjaar.</summary>
    public async Task<FinanceOverviewReport> OverviewAsync(int? carnivalYearId, CancellationToken cancellationToken)
    {
        var year = await ResolveYearAsync(carnivalYearId, cancellationToken);
        if (year is null)
        {
            return new FinanceOverviewReport(0, "", 0m, 0m, 0m, []);
        }

        var sources = await SourcesAsync(year, includeContribution: false, cancellationToken);
        var (total, paid) = Totals(sources);
        return new FinanceOverviewReport(year.Id, year.Name, total, paid, total - paid, sources);
    }

    /// <summary>Statistieken: alles incl. contributie voor het gekozen jaar, met de groei en de reeks over alle carnavalsjaren.</summary>
    public async Task<FinanceStatisticsReport> StatisticsAsync(int? carnivalYearId, CancellationToken cancellationToken)
    {
        var year = await ResolveYearAsync(carnivalYearId, cancellationToken);
        if (year is null)
        {
            return new FinanceStatisticsReport(0, "", 0m, 0m, 0m, null, [], []);
        }

        var years = await db.CarnivalYears.AsNoTracking().OrderBy(y => y.CarnivalEndDate).ToListAsync(cancellationToken);
        var perYear = new List<FinanceYearTotal>(years.Count);
        decimal? previous = null;
        decimal selectedTotal = 0m, selectedPaid = 0m;
        IReadOnlyList<FinanceSourceAmount> selectedSources = [];
        foreach (var y in years)
        {
            var sources = await SourcesAsync(y, includeContribution: true, cancellationToken);
            var (total, paid) = Totals(sources);
            perYear.Add(new FinanceYearTotal(y.Id, Short(y), total));
            if (y.Id == year.Id)
            {
                selectedTotal = total;
                selectedPaid = paid;
                selectedSources = sources;
            }
            else if (y.CarnivalEndDate < year.CarnivalEndDate)
            {
                previous = total; // laatste jaar vóór het gekozen jaar (de lijst is oplopend gesorteerd)
            }
        }

        var growth = previous is > 0m ? Math.Round((selectedTotal - previous.Value) / previous.Value * 100m, 1) : (decimal?)null;
        return new FinanceStatisticsReport(year.Id, year.Name, selectedTotal, selectedPaid, selectedTotal - selectedPaid, growth, selectedSources, perYear);
    }

    /// <summary>De festival-facturen (FEST-betaalverzoeken) van het carnavalsjaar, voor het tabblad op de facturen-pagina.</summary>
    public async Task<IReadOnlyList<FestivalInvoiceRow>> FestivalInvoicesAsync(int? carnivalYearId, CancellationToken cancellationToken)
    {
        var year = await ResolveYearAsync(carnivalYearId, cancellationToken);
        if (year is null)
        {
            return [];
        }

        var prefix = $"FEST-{year.CarnivalEndDate.Year.ToString(CultureInfo.InvariantCulture)}-";
        var rows = await db.DanceFestivalPayments.AsNoTracking()
            .Where(p => p.InvoiceNumber != null && p.InvoiceNumber.StartsWith(prefix))
            .Join(db.DanceFestivalRegistrations.AsNoTracking(), p => p.RegistrationId, r => r.Id, (p, r) => new { p, r.ClubName })
            .OrderBy(x => x.p.InvoiceNumber)
            .ToListAsync(cancellationToken);
        return rows.Select(x => new FestivalInvoiceRow(
            x.p.InvoiceNumber!, x.ClubName, DateOnly.FromDateTime(x.p.CreatedAt), x.p.AmountCents / 100m, FestivalStatus(x.p.Status))).ToList();
    }

    private async Task<IReadOnlyList<FinanceSourceAmount>> SourcesAsync(CarnivalYear year, bool includeContribution, CancellationToken cancellationToken)
    {
        var calendar = year.CarnivalEndDate.Year;

        // Adverteerders — facturen (decimal euro's), zonder de ingetrokken.
        var advertiserRows = db.AdvertiserInvoices.AsNoTracking().Where(i => i.Year == calendar && i.Status != InvoiceStatus.Cancelled);
        var advertiserTotal = await advertiserRows.SumAsync(i => (decimal?)i.TotalInclVat, cancellationToken) ?? 0m;
        var advertiserPaid = await advertiserRows.Where(i => i.Status == InvoiceStatus.Paid).SumAsync(i => (decimal?)i.TotalInclVat, cancellationToken) ?? 0m;

        // Dansgardefestival — betaalverzoeken (cents), op FEST-{jaar}, zonder de ingetrokken.
        var prefix = $"FEST-{calendar.ToString(CultureInfo.InvariantCulture)}-";
        var festivalRows = db.DanceFestivalPayments.AsNoTracking()
            .Where(p => p.InvoiceNumber != null && p.InvoiceNumber.StartsWith(prefix) && p.Status != DanceFestivalPaymentStatus.Canceled);
        var festivalTotal = await festivalRows.SumAsync(p => (long?)p.AmountCents, cancellationToken) ?? 0L;
        var festivalPaid = await festivalRows.Where(p => p.Status == DanceFestivalPaymentStatus.Paid).SumAsync(p => (long?)p.AmountCents, cancellationToken) ?? 0L;

        // Verkoop per soort (cents), op CarnivalYearId; verlopen en geannuleerde tellen niet mee. Pronkzitting,
        // dagkaarten, activiteiten en munten zijn aparte bronnen (via SaleProduct.Kind).
        var saleByKind = await db.SaleOrders.AsNoTracking()
            .Where(o => o.CarnivalYearId == year.Id && o.Status != SaleOrderStatus.Cancelled && o.Status != SaleOrderStatus.Expired)
            .Join(db.SaleProducts.AsNoTracking(), o => o.ProductId, p => p.Id, (o, p) => new { p.Kind, o.Status, o.AmountCents })
            .GroupBy(x => x.Kind)
            .Select(g => new
            {
                Kind = g.Key,
                Total = g.Sum(x => (long)x.AmountCents),
                Paid = g.Sum(x => x.Status == SaleOrderStatus.Confirmed ? (long)x.AmountCents : 0L),
            })
            .ToListAsync(cancellationToken);

        decimal SaleTotal(SaleProductKind kind) => (saleByKind.FirstOrDefault(s => s.Kind == kind)?.Total ?? 0L) / 100m;
        decimal SalePaid(SaleProductKind kind) => (saleByKind.FirstOrDefault(s => s.Kind == kind)?.Paid ?? 0L) / 100m;

        var list = new List<FinanceSourceAmount>
        {
            new(Advertisers, advertiserTotal, advertiserPaid),
            new(Festival, festivalTotal / 100m, festivalPaid / 100m),
            new(Pronkzitting, SaleTotal(SaleProductKind.Pronkzitting), SalePaid(SaleProductKind.Pronkzitting)),
            new(DayTickets, SaleTotal(SaleProductKind.DayTicket), SalePaid(SaleProductKind.DayTicket)),
            new(EventTickets, SaleTotal(SaleProductKind.EventTicket), SalePaid(SaleProductKind.EventTicket)),
            new(Tokens, SaleTotal(SaleProductKind.Tokens), SalePaid(SaleProductKind.Tokens)),
        };

        if (includeContribution)
        {
            // Contributie — geïnde SEPA-runs (decimal) met een incassodatum binnen het seizoen; een bankexport geldt als geïnd.
            var contribution = await db.CollectionRuns.AsNoTracking()
                .Where(r => r.Kind == CollectionKind.Contribution && r.CollectionDate >= year.StartDate && r.CollectionDate <= year.EndDate)
                .SumAsync(r => (decimal?)r.Total, cancellationToken) ?? 0m;
            list.Add(new FinanceSourceAmount(Contribution, contribution, contribution));
        }

        return list;
    }

    private async Task<CarnivalYear?> ResolveYearAsync(int? carnivalYearId, CancellationToken cancellationToken) =>
        carnivalYearId is { } id
            ? await db.CarnivalYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id, cancellationToken)
            : await db.CarnivalYears.AsNoTracking().FirstOrDefaultAsync(y => y.Active, cancellationToken)
              ?? await db.CarnivalYears.AsNoTracking().OrderByDescending(y => y.CarnivalEndDate).FirstOrDefaultAsync(cancellationToken);

    private static (decimal Total, decimal Paid) Totals(IReadOnlyList<FinanceSourceAmount> sources) =>
        (sources.Sum(s => s.Total), sources.Sum(s => s.Paid));

    private static string Short(CarnivalYear year) => year.CarnivalEndDate.Year.ToString(CultureInfo.InvariantCulture);

    private static string FestivalStatus(DanceFestivalPaymentStatus status) => status switch
    {
        DanceFestivalPaymentStatus.Paid => "Betaald",
        DanceFestivalPaymentStatus.Canceled => "Ingetrokken",
        _ => "Open",
    };
}
