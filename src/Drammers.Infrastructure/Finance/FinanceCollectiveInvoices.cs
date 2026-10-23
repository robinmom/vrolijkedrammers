using System.Globalization;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Content.Festival;
using Drammers.Modules.Membership.Advertisers;
using Drammers.Modules.Membership.Finance;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Finance;

/// <summary>Eén betaalde transactie op een verzamelfactuur, bedragen in euro's.</summary>
public sealed record CollectiveInvoiceLineView(string Party, string Reference, DateOnly PaidOn, decimal Amount);

/// <summary>Een gemaakte verzamelfactuur in het kort.</summary>
public sealed record CollectiveInvoiceSummary(Guid Id, string Number, DateOnly InvoiceDate, int LineCount, decimal Total);

/// <summary>Een verzamelfactuur met de regels.</summary>
public sealed record CollectiveInvoiceDetail(CollectiveInvoiceSummary Invoice, string Source, string Year, IReadOnlyList<CollectiveInvoiceLineView> Lines);

/// <summary>Wat er op een volgende (delta)factuur zou komen: de betaalde transacties die nog op geen enkele factuur staan.</summary>
public sealed record CollectiveInvoicePreview(decimal Total, int AlreadyInvoicedCount, IReadOnlyList<CollectiveInvoiceLineView> NewLines);

/// <summary>De verzamelfacturen van een onderdeel in een carnavalsjaar, met de delta die nog gemaakt kan worden.</summary>
public sealed record CollectiveInvoiceOverview(
    int CarnivalYearId, string Year, string Source, IReadOnlyList<CollectiveInvoiceSummary> Invoices, CollectiveInvoicePreview Preview);

/// <summary>
/// Verzamelfacturen (fase 4d): per onderdeel (adverteerders, Dansgardefestival, contributie) en carnavalsjaar één factuur
/// met alle betaalde transacties, regel per regel, met een totaal. Elke factuur wordt bewaard zodat een volgende alleen de
/// sinds de vorige nieuw-betaalde transacties pakt (delta). Contributie geldt als geïnd op de incassodatum van de SEPA-run.
/// </summary>
public sealed class FinanceCollectiveInvoices(DrammersDbContext db, IClock clock, ICurrentActor actor)
{
    private sealed record PaidTransaction(string Key, string Party, string Reference, DateOnly PaidOn, decimal Amount);

    public async Task<CollectiveInvoiceOverview> OverviewAsync(int? carnivalYearId, CollectiveInvoiceSource source, CancellationToken cancellationToken)
    {
        var year = await ResolveYearAsync(carnivalYearId, cancellationToken);
        if (year is null)
        {
            return new CollectiveInvoiceOverview(0, "", SourceLabel(source), [], new CollectiveInvoicePreview(0m, 0, []));
        }

        var invoices = await db.CollectiveInvoices.AsNoTracking()
            .Where(i => i.CarnivalYearId == year.Id && i.Source == source)
            .OrderBy(i => i.Sequence)
            .Select(i => new CollectiveInvoiceSummary(i.Id, i.Number, i.InvoiceDate, i.LineCount, i.Total))
            .ToListAsync(cancellationToken);

        var (newLines, alreadyInvoiced) = await DeltaAsync(year, source, cancellationToken);
        var preview = new CollectiveInvoicePreview(
            newLines.Sum(l => l.Amount), alreadyInvoiced,
            [.. newLines.Select(l => new CollectiveInvoiceLineView(l.Party, l.Reference, l.PaidOn, l.Amount))]);
        return new CollectiveInvoiceOverview(year.Id, year.Name, SourceLabel(source), invoices, preview);
    }

    public async Task<CollectiveInvoiceDetail> DetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.CollectiveInvoices.AsNoTracking().Include(i => i.Lines).SingleOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "De verzamelfactuur bestaat niet.", DomainErrorKind.NotFound);
        var year = await db.CarnivalYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == invoice.CarnivalYearId, cancellationToken);
        var lines = invoice.Lines.OrderBy(l => l.SortOrder)
            .Select(l => new CollectiveInvoiceLineView(l.Party, l.Reference, l.PaidOn, l.Amount)).ToList();
        return new CollectiveInvoiceDetail(
            new CollectiveInvoiceSummary(invoice.Id, invoice.Number, invoice.InvoiceDate, invoice.LineCount, invoice.Total),
            SourceLabel(invoice.Source), year?.Name ?? "", lines);
    }

    /// <summary>Maakt de volgende (delta)factuur met alleen de nieuw-betaalde transacties; weigert als er niets nieuws is.</summary>
    public async Task<Guid> GenerateAsync(int? carnivalYearId, CollectiveInvoiceSource source, CancellationToken cancellationToken)
    {
        var year = await ResolveYearAsync(carnivalYearId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.Validation, "Er is geen carnavalsjaar gekozen.", DomainErrorKind.Validation);

        var (newLines, _) = await DeltaAsync(year, source, cancellationToken);
        if (newLines.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Er zijn geen nieuwe betaalde transacties sinds de vorige verzamelfactuur.", DomainErrorKind.Conflict);
        }

        var sequence = await db.CollectiveInvoices.CountAsync(i => i.CarnivalYearId == year.Id && i.Source == source, cancellationToken) + 1;
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var invoice = new CollectiveInvoice
        {
            Id = IdGenerator.NewId(),
            CarnivalYearId = year.Id,
            Source = source,
            Number = $"VF-{year.CarnivalEndDate.Year.ToString(CultureInfo.InvariantCulture)}-{SourceCode(source)}-{sequence.ToString("00", CultureInfo.InvariantCulture)}",
            Sequence = sequence,
            InvoiceDate = today,
            Total = newLines.Sum(l => l.Amount),
            LineCount = newLines.Count,
            CreatedAt = clock.UtcNow.UtcDateTime,
            CreatedBy = actor.UserId,
            Lines = [.. newLines.Select((l, i) => new CollectiveInvoiceLine
            {
                Id = IdGenerator.NewId(),
                TransactionKey = l.Key,
                Party = l.Party,
                Reference = l.Reference,
                PaidOn = l.PaidOn,
                Amount = l.Amount,
                SortOrder = i,
            })],
        };
        db.CollectiveInvoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice.Id;
    }

    private async Task<(IReadOnlyList<PaidTransaction> NewLines, int AlreadyInvoiced)> DeltaAsync(
        CarnivalYear year, CollectiveInvoiceSource source, CancellationToken cancellationToken)
    {
        var paid = await PaidTransactionsAsync(year, source, cancellationToken);
        var invoicedKeys = await db.CollectiveInvoiceLines.AsNoTracking()
            .Where(l => db.CollectiveInvoices.Any(i => i.Id == l.InvoiceId && i.CarnivalYearId == year.Id && i.Source == source))
            .Select(l => l.TransactionKey)
            .ToListAsync(cancellationToken);
        var invoiced = invoicedKeys.ToHashSet(StringComparer.Ordinal);
        var newLines = paid.Where(p => !invoiced.Contains(p.Key)).ToList();
        return (newLines, paid.Count - newLines.Count);
    }

    private async Task<IReadOnlyList<PaidTransaction>> PaidTransactionsAsync(CarnivalYear year, CollectiveInvoiceSource source, CancellationToken cancellationToken)
    {
        var calendar = year.CarnivalEndDate.Year;
        switch (source)
        {
            case CollectiveInvoiceSource.Advertisers:
                {
                    var rows = await db.AdvertiserInvoices.AsNoTracking()
                        .Where(i => i.Year == calendar && i.Status == InvoiceStatus.Paid && i.PaidAt != null)
                        .OrderBy(i => i.PaidAt).ThenBy(i => i.Number)
                        .Select(i => new { i.Number, i.CompanyName, i.PaidAt, i.TotalInclVat })
                        .ToListAsync(cancellationToken);
                    return [.. rows.Select(r => new PaidTransaction(
                    $"ADV:{r.Number}", r.CompanyName, r.Number, DateOnly.FromDateTime(r.PaidAt!.Value), r.TotalInclVat))];
                }

            case CollectiveInvoiceSource.Festival:
                {
                    var prefix = $"FEST-{calendar.ToString(CultureInfo.InvariantCulture)}-";
                    var rows = await db.DanceFestivalPayments.AsNoTracking()
                        .Where(p => p.InvoiceNumber != null && p.InvoiceNumber.StartsWith(prefix) && p.Status == DanceFestivalPaymentStatus.Paid && p.PaidAt != null)
                        .Join(db.DanceFestivalRegistrations.AsNoTracking(), p => p.RegistrationId, r => r.Id, (p, r) => new { p.InvoiceNumber, r.ClubName, p.PaidAt, p.AmountCents })
                        .OrderBy(x => x.PaidAt).ThenBy(x => x.InvoiceNumber)
                        .ToListAsync(cancellationToken);
                    return [.. rows.Select(r => new PaidTransaction(
                    $"FEST:{r.InvoiceNumber}", r.ClubName, r.InvoiceNumber!, DateOnly.FromDateTime(r.PaidAt!.Value), r.AmountCents / 100m))];
                }

            default:
                {
                    // Contributie: een geïnde SEPA-run (incassodatum binnen het seizoen); elke regel is één lid.
                    var rows = await db.CollectionRunLines.AsNoTracking()
                        .Join(db.CollectionRuns.AsNoTracking(), l => l.RunId, r => r.Id, (l, r) => new { Line = l, Run = r })
                        .Where(x => x.Run.Kind == CollectionKind.Contribution && x.Run.CollectionDate >= year.StartDate && x.Run.CollectionDate <= year.EndDate)
                        .OrderBy(x => x.Run.CollectionDate).ThenBy(x => x.Line.DebtorName)
                        .Select(x => new { x.Line.Id, x.Line.DebtorName, x.Line.MemberNumber, x.Run.CollectionDate, x.Line.Amount })
                        .ToListAsync(cancellationToken);
                    return [.. rows.Select(r => new PaidTransaction(
                    $"CONTRIB:{r.Id.ToString(CultureInfo.InvariantCulture)}", r.DebtorName, r.MemberNumber, r.CollectionDate, r.Amount))];
                }
        }
    }

    private async Task<CarnivalYear?> ResolveYearAsync(int? carnivalYearId, CancellationToken cancellationToken) =>
        carnivalYearId is { } id
            ? await db.CarnivalYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id, cancellationToken)
            : await db.CarnivalYears.AsNoTracking().FirstOrDefaultAsync(y => y.Active, cancellationToken)
              ?? await db.CarnivalYears.AsNoTracking().OrderByDescending(y => y.CarnivalEndDate).FirstOrDefaultAsync(cancellationToken);

    public static string SourceLabel(CollectiveInvoiceSource source) => source switch
    {
        CollectiveInvoiceSource.Advertisers => "Adverteerders",
        CollectiveInvoiceSource.Festival => "Dansgardefestival",
        _ => "Contributie",
    };

    private static string SourceCode(CollectiveInvoiceSource source) => source switch
    {
        CollectiveInvoiceSource.Advertisers => "ADV",
        CollectiveInvoiceSource.Festival => "FEST",
        _ => "CONTR",
    };
}
