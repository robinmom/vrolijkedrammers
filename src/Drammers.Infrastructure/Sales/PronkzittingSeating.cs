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

/// <summary>
/// Eén rij tafels (rij 0 staat bij het podium), met het totaal aantal plaatsen in die rij. <see cref="Chairs"/> zijn losse
/// stoelen ná de laatste tafel: als er minder dan vier plaatsen overblijven komt er geen extra tafel maar zetten we stoelen neer.
/// </summary>
public sealed record SeatingRow(int RowIndex, int Seats, int TableCount, IReadOnlyList<SeatingTable> Tables, IReadOnlyList<SeatingTablePart> Chairs);

/// <summary>
/// Eén gezelschap (groep of losse bestelling) met z'n plaats, de tafels waarop het zit en een vaste kleur.
/// <see cref="StartTable"/> is de tafel waarop het gezelschap begint; <see cref="Manual"/> geeft aan of het bestuur die
/// plaats zelf heeft gekozen (anders automatisch ingedeeld). <see cref="ChairSeats"/> is het aantal plaatsen op losse stoelen.
/// </summary>
public sealed record SeatingParty(
    string Key, string Name, bool IsGroup, int Seats, int RowIndex, int StartTable, bool Manual, string? Remark, int Color,
    IReadOnlyList<int> Tables, int ChairSeats);

/// <summary>De volledige tafelindeling van één avond: de plattegrond (rijen met tafels) én de lijst gezelschappen (afdruklijst).</summary>
public sealed record SeatingLayout(
    Guid ProductId, string ProductName, DateOnly? Date, int TablesPerRow, int SeatsPerTable, int RowCount, int TotalSeats, int? Capacity,
    bool Overflow, bool Conflict, IReadOnlyList<SeatingRow> Rows, IReadOnlyList<SeatingParty> Parties);

/// <summary>
/// Tafelindeling van de pronkzitting (fase 19t): elke groep of losse bestelling zit bij elkaar, altijd vier rijen met acht
/// plaatsen per tafel en een instelbaar aantal tafels per rij. Het bestuur kan een gezelschap op een gekozen starttafel
/// zetten (ook als eerdere tafels nog leeg zijn); een gezelschap bezet van daaraf aaneengesloten plaatsen en kan over
/// meerdere tafels lopen. Gezelschappen zónder gekozen plaats worden automatisch in de vrije plekken gevuld (ook vóór een
/// handmatig geplaatst gezelschap). Het resultaat is als afdruklijst (gezelschap → tafelnummers) uit te draaien.
/// </summary>
public sealed class PronkzittingSeating(DrammersDbContext db, IAuditLogger audit, IClock clock)
{
    public const int RowCount = 4;
    public const int SeatsPerTable = 8;
    public const int DefaultTablesPerRow = 6;

    /// <summary>Blijven er na de laatste tafel minder dan zoveel plaatsen over, dan losse stoelen i.p.v. een extra tafel.</summary>
    public const int ChairThreshold = 4;
    private const int Palette = 10;

    /// <summary>De tafelindeling van een avond opbouwen: gekozen plaatsen gerespecteerd, de rest in de vrije plekken gevuld.</summary>
    public async Task<SeatingLayout> LayoutAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await ProductAsync(productId, cancellationToken);
        var tablesPerRow = await db.PronkzittingSeatingLayouts.AsNoTracking().Where(l => l.ProductId == productId)
            .Select(l => (int?)l.TablesPerRow).SingleOrDefaultAsync(cancellationToken) ?? DefaultTablesPerRow;
        var assignments = await db.PronkzittingSeats.AsNoTracking().Where(s => s.ProductId == productId)
            .ToDictionaryAsync(s => s.PartyKey, s => (s.RowIndex, s.StartSeat), cancellationToken);
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

    /// <summary>
    /// Een gezelschap op een starttafel zetten (<paramref name="table"/> is het tafelnummer, bijv. 205), of weer automatisch
    /// laten indelen (<paramref name="table"/> is <c>null</c>).
    /// </summary>
    public async Task PlaceAsync(Guid productId, string partyKey, int? table, CancellationToken cancellationToken)
    {
        await ProductAsync(productId, cancellationToken);
        var parties = await PartiesAsync(productId, cancellationToken);
        if (parties.All(p => p.Key != partyKey))
        {
            throw new DomainException(ErrorCodes.NotFound, "Dit gezelschap staat niet in de indeling.", DomainErrorKind.NotFound);
        }

        var seat = await db.PronkzittingSeats.SingleOrDefaultAsync(s => s.ProductId == productId && s.PartyKey == partyKey, cancellationToken);
        if (table is null)
        {
            // Automatisch: de gekozen plaats loslaten.
            if (seat is not null)
            {
                db.PronkzittingSeats.Remove(seat);
                await db.SaveChangesAsync(cancellationToken);
                await audit.WriteAsync(new AuditEntry("sale.seating-place", "SaleProduct", productId.ToString(), null, $"{partyKey} → automatisch"), cancellationToken);
            }

            return;
        }

        var row = table.Value / 100 - 1;
        var indexInRow = table.Value % 100 - 1;
        if (row is < 0 or >= RowCount || indexInRow < 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Die tafel bestaat niet.", DomainErrorKind.Validation);
        }

        var startSeat = indexInRow * SeatsPerTable;
        if (seat is null)
        {
            db.PronkzittingSeats.Add(new PronkzittingSeat { Id = IdGenerator.NewId(), ProductId = productId, PartyKey = partyKey, RowIndex = (byte)row, StartSeat = startSeat });
        }
        else
        {
            seat.RowIndex = (byte)row;
            seat.StartSeat = startSeat;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("sale.seating-place", "SaleProduct", productId.ToString(), null, $"{partyKey} → tafel {table}"), cancellationToken);
    }

    /// <summary>De hele indeling automatisch (opnieuw) verdelen: alle gekozen plaatsen wissen.</summary>
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
        SaleProduct product, int tablesPerRow, IReadOnlyList<PartyInput> parties, IReadOnlyDictionary<string, (byte RowIndex, int StartSeat)> assignments)
    {
        var rowCapacity = tablesPerRow * SeatsPerTable;
        var occupied = new List<(int Start, int End)>[RowCount];
        for (var r = 0; r < RowCount; r++)
        {
            occupied[r] = [];
        }

        var placed = new List<(PartyInput Party, int Row, int Start, bool Manual)>();
        var conflict = false;

        // 1) De gekozen plaatsen eerst, op (rij, starttafel). Botst een keuze met een al geplaatst gezelschap, dan schuift hij
        //    door naar de eerste vrije plaats erna (en melden we een conflict, zodat het bestuur het kan rechtzetten).
        foreach (var (party, assignment) in parties.Where(p => assignments.ContainsKey(p.Key))
                     .Select(p => (p, assignments[p.Key]))
                     .OrderBy(x => x.Item2.RowIndex).ThenBy(x => x.Item2.StartSeat))
        {
            var row = Math.Clamp((int)assignment.RowIndex, 0, RowCount - 1);
            var start = Math.Max(0, assignment.StartSeat);
            while (occupied[row].Any(o => start < o.End && o.Start < start + party.Seats))
            {
                start = occupied[row].Where(o => start < o.End && o.Start < start + party.Seats).Max(o => o.End);
                conflict = true;
            }

            occupied[row].Add((start, start + party.Seats));
            placed.Add((party, row, start, true));
        }

        // 2) De rest automatisch: grootste eerst, in de vroegste rij (links→rechts) met een vrij gat dat groot genoeg is —
        //    zo vullen kleine bestellingen de gaten op, ook vóór een handmatig geplaatst gezelschap. Past het nergens binnen
        //    het ingestelde aantal tafels, dan achteraan in de minst gevulde rij (overloop met extra tafels).
        foreach (var party in parties.Where(p => !assignments.ContainsKey(p.Key))
                     .OrderByDescending(p => p.Seats).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var chosenRow = -1;
            var chosenStart = -1;
            for (var r = 0; r < RowCount && chosenRow < 0; r++)
            {
                var gap = FindGap(occupied[r], rowCapacity, party.Seats);
                if (gap >= 0)
                {
                    chosenRow = r;
                    chosenStart = gap;
                }
            }

            if (chosenRow < 0)
            {
                chosenRow = 0;
                for (var r = 1; r < RowCount; r++)
                {
                    if (SeatsIn(occupied[r]) < SeatsIn(occupied[chosenRow]))
                    {
                        chosenRow = r;
                    }
                }

                chosenStart = occupied[chosenRow].Count == 0 ? 0 : occupied[chosenRow].Max(o => o.End);
            }

            occupied[chosenRow].Add((chosenStart, chosenStart + party.Seats));
            placed.Add((party, chosenRow, chosenStart, false));
        }

        // Per rij: waar beginnen de losse stoelen? Als de laatste (overloop)tafel minder dan ChairThreshold plaatsen heeft,
        // tonen we die staart als losse stoelen ná de laatste tafel i.p.v. als extra tafel.
        var chairsStart = new int[RowCount];
        for (var r = 0; r < RowCount; r++)
        {
            var maxSeat = occupied[r].Count == 0 ? 0 : occupied[r].Max(o => o.End);
            var lastTableIndex = maxSeat == 0 ? -1 : (maxSeat - 1) / SeatsPerTable;
            var seatsInLast = maxSeat - lastTableIndex * SeatsPerTable;
            chairsStart[r] = lastTableIndex >= tablesPerRow && seatsInLast < ChairThreshold ? lastTableIndex * SeatsPerTable : int.MaxValue;
        }

        var seatingParties = new List<SeatingParty>();
        var rowTableParts = new Dictionary<int, List<SeatingTablePart>>[RowCount];
        var rowTableSeats = new Dictionary<int, int>[RowCount];
        var rowChairs = new List<SeatingTablePart>[RowCount];
        var rowMaxTable = new int[RowCount];
        for (var r = 0; r < RowCount; r++)
        {
            rowTableParts[r] = [];
            rowTableSeats[r] = [];
            rowChairs[r] = [];
            rowMaxTable[r] = -1;
        }

        // Van links naar rechts, zodat de volgorde van de stoelen in een tafel klopt (eerste gezelschap vooraan).
        foreach (var (party, row, start, manual) in placed.OrderBy(x => x.Row).ThenBy(x => x.Start))
        {
            var end = start + party.Seats;
            var color = Color(party.Key);
            var tables = new List<int>();

            // Plaatsen die nog op een genummerde tafel vallen (vóór de losse stoelen).
            var tableEnd = Math.Min(end, chairsStart[row]);
            if (tableEnd > start)
            {
                var firstTable = start / SeatsPerTable;
                var lastTable = (tableEnd - 1) / SeatsPerTable;
                for (var t = firstTable; t <= lastTable; t++)
                {
                    var seatsHere = Math.Min(tableEnd, (t + 1) * SeatsPerTable) - Math.Max(start, t * SeatsPerTable);
                    rowTableParts[row].TryAdd(t, []);
                    rowTableParts[row][t].Add(new SeatingTablePart(party.Key, party.Name, party.IsGroup, color, seatsHere));
                    rowTableSeats[row][t] = rowTableSeats[row].GetValueOrDefault(t) + seatsHere;
                    tables.Add(TableNumber(row, t));
                    rowMaxTable[row] = Math.Max(rowMaxTable[row], t);
                }
            }

            // Plaatsen op losse stoelen (de staart die niet meer op een tafel komt).
            var chairSeats = end - Math.Max(start, chairsStart[row]);
            if (chairSeats > 0)
            {
                rowChairs[row].Add(new SeatingTablePart(party.Key, party.Name, party.IsGroup, color, chairSeats));
            }

            seatingParties.Add(new SeatingParty(party.Key, party.Name, party.IsGroup, party.Seats, row, TableNumber(row, start / SeatsPerTable),
                manual, party.Remark, color, tables, Math.Max(0, chairSeats)));
        }

        var seatingRows = new List<SeatingRow>();
        var anyOverflow = false;
        for (var r = 0; r < RowCount; r++)
        {
            var maxNumberedTable = chairsStart[r] == int.MaxValue ? rowMaxTable[r] : chairsStart[r] / SeatsPerTable - 1;
            var tableCount = Math.Max(tablesPerRow, maxNumberedTable + 1);
            var rowTables = new List<SeatingTable>();
            for (var t = 0; t < tableCount; t++)
            {
                var overflow = t >= tablesPerRow;
                anyOverflow |= overflow && rowTableParts[r].ContainsKey(t);
                rowTables.Add(new SeatingTable(TableNumber(r, t), r, t, rowTableSeats[r].GetValueOrDefault(t),
                    rowTableParts[r].GetValueOrDefault(t)?.Count > 1, overflow, rowTableParts[r].GetValueOrDefault(t) ?? []));
            }

            seatingRows.Add(new SeatingRow(r, SeatsIn(occupied[r]), tableCount, rowTables, rowChairs[r]));
        }

        var orderedParties = seatingParties
            .OrderBy(p => p.RowIndex).ThenBy(p => p.StartTable).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new SeatingLayout(product.Id, product.Name, product.Date, tablesPerRow, SeatsPerTable, RowCount,
            parties.Sum(p => p.Seats), product.Capacity, anyOverflow, conflict, seatingRows, orderedParties);
    }

    /// <summary>De vroegste vrije plaats in een rij waar <paramref name="seats"/> plaatsen aaneengesloten passen binnen de capaciteit, anders -1.</summary>
    private static int FindGap(List<(int Start, int End)> occupied, int capacity, int seats)
    {
        var cursor = 0;
        foreach (var (start, end) in occupied.OrderBy(o => o.Start))
        {
            if (start - cursor >= seats)
            {
                return cursor;
            }

            cursor = Math.Max(cursor, end);
        }

        return capacity - cursor >= seats ? cursor : -1;
    }

    private static int SeatsIn(List<(int Start, int End)> occupied) => occupied.Sum(o => o.End - o.Start);

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
