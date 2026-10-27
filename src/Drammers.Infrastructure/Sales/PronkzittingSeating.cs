using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Ticketing.Sales;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Sales;

/// <summary>Het aandeel van één gezelschap in één tafel (voor een gedeelde tafel staan er meerdere onder elkaar).</summary>
public sealed record SeatingTablePart(string PartyKey, string PartyName, bool IsGroup, int Color, int Seats);

/// <summary>Eén tafel in de plattegrond: het nummer (101, 102, … / 201, …), de rij en wie er zit.</summary>
public sealed record SeatingTable(int Number, int RowIndex, int IndexInRow, int Seats, bool Shared, bool Overflow, IReadOnlyList<SeatingTablePart> Parts);

/// <summary>Eén rij tafels (rij 0 staat bij het podium), met het totaal aantal plaatsen in die rij.</summary>
public sealed record SeatingRow(int RowIndex, int Seats, int TableCount, IReadOnlyList<SeatingTable> Tables);

/// <summary>Eén gezelschap (groep of losse bestelling) met z'n plaats, de tafels waarop het zit en een vaste kleur.</summary>
public sealed record SeatingParty(
    string Key, string Name, bool IsGroup, int Seats, int RowIndex, int Position, bool Manual, string? Remark, int Color, IReadOnlyList<int> Tables);

/// <summary>De volledige tafelindeling van één avond: de plattegrond (rijen met tafels) én de lijst gezelschappen (afdruklijst).</summary>
public sealed record SeatingLayout(
    Guid ProductId, string ProductName, DateOnly? Date, int TablesPerRow, int SeatsPerTable, int RowCount, int TotalSeats, int? Capacity,
    bool Overflow, IReadOnlyList<SeatingRow> Rows, IReadOnlyList<SeatingParty> Parties);

/// <summary>
/// Tafelindeling van de pronkzitting (fase 19t): elke groep of losse bestelling zit bij elkaar, altijd vier rijen met acht
/// plaatsen per tafel en een instelbaar aantal tafels per rij. Een gezelschap blijft in dezelfde rij en kan over meerdere
/// tafels lopen; kleine bestellingen vullen de lege plaatsen aan, zodat een tafel door twee gezelschappen gedeeld kan worden.
/// Wie nog geen plaats heeft wordt automatisch ingedeeld: de rijen lopen op volgorde vol (links→rechts) voordat de volgende
/// begint. Het bestuur kan gezelschappen verslepen en de indeling als afdruklijst (gezelschap → tafelnummers) uitdraaien.
/// </summary>
public sealed class PronkzittingSeating(DrammersDbContext db, IAuditLogger audit, IClock clock)
{
    public const int RowCount = 4;
    public const int SeatsPerTable = 8;
    public const int DefaultTablesPerRow = 6;
    private const int Palette = 10;

    /// <summary>De tafelindeling van een avond opbouwen: bewaarde plaatsen gerespecteerd, de rest gebalanceerd ingedeeld.</summary>
    public async Task<SeatingLayout> LayoutAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await ProductAsync(productId, cancellationToken);
        var tablesPerRow = await db.PronkzittingSeatingLayouts.AsNoTracking().Where(l => l.ProductId == productId)
            .Select(l => (int?)l.TablesPerRow).SingleOrDefaultAsync(cancellationToken) ?? DefaultTablesPerRow;
        var assignments = await db.PronkzittingSeats.AsNoTracking().Where(s => s.ProductId == productId)
            .ToDictionaryAsync(s => s.PartyKey, s => (s.RowIndex, s.Position), cancellationToken);
        var parties = await PartiesAsync(productId, cancellationToken);

        return Build(product, tablesPerRow, parties, assignments);
    }

    /// <summary>Het aantal tafels per rij instellen (1..50).</summary>
    public async Task SetTablesPerRowAsync(Guid productId, int tablesPerRow, CancellationToken cancellationToken)
    {
        await ProductAsync(productId, cancellationToken);
        if (tablesPerRow is < 1 or > 50)
        {
            throw new DomainException(ErrorCodes.Validation, "Het aantal tafels per rij moet tussen 1 en 50 liggen.", DomainErrorKind.Validation);
        }

        var layout = await db.PronkzittingSeatingLayouts.SingleOrDefaultAsync(l => l.ProductId == productId, cancellationToken);
        if (layout is null)
        {
            db.PronkzittingSeatingLayouts.Add(new PronkzittingSeatingLayout { ProductId = productId, TablesPerRow = tablesPerRow, UpdatedAt = clock.UtcNow.UtcDateTime });
        }
        else
        {
            layout.TablesPerRow = tablesPerRow;
            layout.UpdatedAt = clock.UtcNow.UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("sale.seating-tables", "SaleProduct", productId.ToString(), null, $"{tablesPerRow} tafels per rij"), cancellationToken);
    }

    /// <summary>Een gezelschap naar een rij en plaats verslepen; de volgorde in de betrokken rijen wordt vastgelegd.</summary>
    public async Task MoveAsync(Guid productId, string partyKey, int rowIndex, int position, CancellationToken cancellationToken)
    {
        var product = await ProductAsync(productId, cancellationToken);
        if (rowIndex is < 0 or >= RowCount)
        {
            throw new DomainException(ErrorCodes.Validation, "De rij bestaat niet.", DomainErrorKind.Validation);
        }

        var tablesPerRow = await db.PronkzittingSeatingLayouts.AsNoTracking().Where(l => l.ProductId == productId)
            .Select(l => (int?)l.TablesPerRow).SingleOrDefaultAsync(cancellationToken) ?? DefaultTablesPerRow;
        var assignments = await db.PronkzittingSeats.Where(s => s.ProductId == productId).ToListAsync(cancellationToken);
        var assignmentMap = assignments.ToDictionary(s => s.PartyKey, s => (s.RowIndex, s.Position));
        var parties = await PartiesAsync(productId, cancellationToken);
        var layout = Build(product, tablesPerRow, parties, assignmentMap);

        var moving = layout.Parties.SingleOrDefault(p => p.Key == partyKey)
            ?? throw new DomainException(ErrorCodes.NotFound, "Dit gezelschap staat niet in de indeling.", DomainErrorKind.NotFound);

        var sourceRow = moving.RowIndex;
        // De huidige volgorde per rij (links naar rechts) als basis; het verplaatste gezelschap eruit en op de nieuwe plek erin.
        var rows = Enumerable.Range(0, RowCount).ToDictionary(r => r,
            r => layout.Parties.Where(p => p.RowIndex == r).OrderBy(p => p.Position).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(p => p.Key).ToList());
        rows[sourceRow].Remove(partyKey);
        var target = rows[rowIndex];
        target.Insert(Math.Clamp(position, 0, target.Count), partyKey);

        // Alleen de bron- en doelrij vastleggen (0..n); andere rijen blijven zoals ze stonden.
        foreach (var r in new[] { sourceRow, rowIndex }.Distinct())
        {
            for (var i = 0; i < rows[r].Count; i++)
            {
                var key = rows[r][i];
                var existing = assignments.SingleOrDefault(s => s.PartyKey == key);
                if (existing is null)
                {
                    db.PronkzittingSeats.Add(new PronkzittingSeat { Id = IdGenerator.NewId(), ProductId = productId, PartyKey = key, RowIndex = (byte)r, Position = i });
                }
                else
                {
                    existing.RowIndex = (byte)r;
                    existing.Position = i;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("sale.seating-move", "SaleProduct", productId.ToString(), null, $"{moving.Name} → rij {rowIndex + 1}"), cancellationToken);
    }

    /// <summary>De hele indeling automatisch (opnieuw) verdelen: alle handmatige plaatsen wissen en gebalanceerd indelen.</summary>
    public async Task AutoArrangeAsync(Guid productId, CancellationToken cancellationToken)
    {
        await ProductAsync(productId, cancellationToken);
        var assignments = await db.PronkzittingSeats.Where(s => s.ProductId == productId).ToListAsync(cancellationToken);
        if (assignments.Count > 0)
        {
            db.PronkzittingSeats.RemoveRange(assignments);
            await db.SaveChangesAsync(cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("sale.seating-arrange", "SaleProduct", productId.ToString(), null, "automatisch verdeeld"), cancellationToken);
    }

    private async Task<SaleProduct> ProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await db.SaleProducts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "De avond bestaat niet.", DomainErrorKind.NotFound);
        if (product.Kind != SaleProductKind.Pronkzitting)
        {
            throw new DomainException(ErrorCodes.Validation, "Dit product is geen pronkzitting.", DomainErrorKind.Validation);
        }

        return product;
    }

    /// <summary>De gezelschappen van een avond: groepen (op groepsnaam) en losse bestellingen, met het aantal verkochte plaatsen.</summary>
    private async Task<List<PartyInput>> PartiesAsync(Guid productId, CancellationToken cancellationToken)
    {
        var orders = await db.SaleOrders.AsNoTracking()
            .Where(o => o.ProductId == productId && o.Status == SaleOrderStatus.Confirmed)
            .OrderBy(o => o.CreatedAt).ToListAsync(cancellationToken);

        static string? Join(IEnumerable<string?> values) =>
            string.Join(" · ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase)) is { Length: > 0 } s ? s : null;

        var groups = orders.Where(o => o.GroupName is not null)
            .GroupBy(o => o.GroupName!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PartyInput($"G:{g.First().GroupName}", g.First().GroupName!, true, g.Sum(o => o.Quantity), Join(g.Select(o => o.Remark))));
        var loose = orders.Where(o => o.GroupName is null)
            .Select(o => new PartyInput($"O:{o.Id}", o.BuyerName, false, o.Quantity, o.Remark));

        return [.. groups.Concat(loose).Where(p => p.Seats > 0)];
    }

    private static SeatingLayout Build(
        SaleProduct product, int tablesPerRow, IReadOnlyList<PartyInput> parties, IReadOnlyDictionary<string, (byte RowIndex, int Position)> assignments)
    {
        // Elke rij krijgt een lopende lijst gezelschappen. Bewaarde plaatsen eerst; de rest gebalanceerd op de rij met de minste plaatsen.
        var rowSeats = new int[RowCount];
        var rowParties = Enumerable.Range(0, RowCount).Select(_ => new List<(PartyInput Party, int Position, bool Manual)>()).ToArray();

        foreach (var party in parties.Where(p => assignments.ContainsKey(p.Key)))
        {
            var (row, pos) = assignments[party.Key];
            var r = Math.Clamp((int)row, 0, RowCount - 1);
            rowParties[r].Add((party, pos, true));
            rowSeats[r] += party.Seats;
        }

        // De automatisch in te delen gezelschappen: grootste eerst, en de rijen worden op volgorde gevuld — elk gezelschap
        // komt in de eerste rij (links→rechts) waar het nog helemaal in past, zodat een rij volledig volloopt voordat de
        // volgende begint en kleine bestellingen achter grote aanschuiven. Past niemand meer? Dan de rij met de meeste ruimte over.
        var rowCapacity = tablesPerRow * SeatsPerTable;
        foreach (var party in parties.Where(p => !assignments.ContainsKey(p.Key))
                     .OrderByDescending(p => p.Seats).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var r = -1;
            for (var i = 0; i < RowCount; i++)
            {
                if (rowSeats[i] + party.Seats <= rowCapacity)
                {
                    r = i;
                    break;
                }
            }

            if (r < 0)
            {
                r = 0;
                for (var i = 1; i < RowCount; i++)
                {
                    if (rowSeats[i] < rowSeats[r])
                    {
                        r = i;
                    }
                }
            }

            var nextPos = rowParties[r].Count == 0 ? 0 : rowParties[r].Max(x => x.Position) + 1;
            rowParties[r].Add((party, nextPos, false));
            rowSeats[r] += party.Seats;
        }

        var seatingParties = new List<SeatingParty>();
        var seatingRows = new List<SeatingRow>();
        var anyOverflow = false;

        for (var r = 0; r < RowCount; r++)
        {
            var ordered = rowParties[r].OrderBy(x => x.Position).ThenBy(x => x.Party.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var tableParts = new Dictionary<int, List<SeatingTablePart>>();
            var tableSeats = new Dictionary<int, int>();
            var seatOffset = 0;
            var maxTableIndex = -1;

            for (var pi = 0; pi < ordered.Count; pi++)
            {
                var (party, _, manual) = ordered[pi];
                var start = seatOffset;
                var end = seatOffset + party.Seats; // exclusief
                var firstTable = start / SeatsPerTable;
                var lastTable = (end - 1) / SeatsPerTable;
                var tables = new List<int>();
                for (var t = firstTable; t <= lastTable; t++)
                {
                    var seatsHere = Math.Min(end, (t + 1) * SeatsPerTable) - Math.Max(start, t * SeatsPerTable);
                    tableParts.TryAdd(t, []);
                    tableParts[t].Add(new SeatingTablePart(party.Key, party.Name, party.IsGroup, Color(party.Key), seatsHere));
                    tableSeats[t] = tableSeats.GetValueOrDefault(t) + seatsHere;
                    tables.Add(TableNumber(r, t));
                    maxTableIndex = Math.Max(maxTableIndex, t);
                }

                seatingParties.Add(new SeatingParty(party.Key, party.Name, party.IsGroup, party.Seats, r, pi, manual, party.Remark, Color(party.Key), tables));
                seatOffset = end;
            }

            // Toon minstens het ingestelde aantal tafels; extra (overvolle) tafels komen erbij zodat niemand wegvalt.
            var tableCount = Math.Max(tablesPerRow, maxTableIndex + 1);
            var rowTables = new List<SeatingTable>();
            for (var t = 0; t < tableCount; t++)
            {
                var overflow = t >= tablesPerRow;
                anyOverflow |= overflow && tableParts.ContainsKey(t);
                rowTables.Add(new SeatingTable(TableNumber(r, t), r, t, tableSeats.GetValueOrDefault(t), tableParts.GetValueOrDefault(t)?.Count > 1,
                    overflow, tableParts.GetValueOrDefault(t) ?? []));
            }

            seatingRows.Add(new SeatingRow(r, rowSeats[r], tableCount, rowTables));
        }

        var orderedParties = seatingParties
            .OrderBy(p => p.RowIndex).ThenBy(p => p.Position).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new SeatingLayout(product.Id, product.Name, product.Date, tablesPerRow, SeatsPerTable, RowCount,
            parties.Sum(p => p.Seats), product.Capacity, anyOverflow, seatingRows, orderedParties);
    }

    private static int TableNumber(int rowIndex, int tableIndex) => (rowIndex + 1) * 100 + tableIndex + 1;

    /// <summary>Een vaste kleurindex (0..Palette-1) per gezelschap, stabiel over herladen (FNV-1a over de sleutel).</summary>
    private static int Color(string key)
    {
        uint hash = 2166136261;
        foreach (var ch in key)
        {
            hash = (hash ^ ch) * 16777619;
        }

        return (int)(hash % Palette);
    }

    private sealed record PartyInput(string Key, string Name, bool IsGroup, int Seats, string? Remark);
}
