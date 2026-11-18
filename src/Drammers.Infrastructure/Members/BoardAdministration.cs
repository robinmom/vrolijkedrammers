using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Board;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

public sealed record BoardBodyInput(string Name, bool HasTerms, int Position);

public sealed record BoardBodyView(Guid Id, string Name, bool HasTerms, int Position, int Seats);

public sealed record BoardTermInput(DateOnly StartDate, int ExtensionYears, string? Note);

public sealed record BoardSeatInput(
    Guid BodyId, Guid? MemberId, string Name, string? Function, DateOnly StartDate, DateOnly? EndDate, string? Notes,
    IReadOnlyList<BoardTermInput> Terms);

public sealed record BoardTermView(int Number, DateOnly StartDate, DateOnly EndDate, int ExtensionYears, string? Note, bool IsCurrent);

/// <summary>
/// Een plek in het kader, met alles wat het portal nodig heeft om de balk te tekenen: hoeveel dagen iemand er in zit,
/// in welke periode hij nu zit en waar die eindigt.
/// </summary>
public sealed record BoardSeatView(
    Guid Id, Guid BodyId, string Body, bool HasTerms, Guid? MemberId, string Name, string? Function,
    DateOnly StartDate, DateOnly? EndDate, string? Notes, int DaysServed, int? TermNumber, DateOnly? TermStart,
    DateOnly? TermEnd, int? DaysUntilTermEnd, int TermExtensionYears, IReadOnlyList<BoardTermView> Terms);

/// <summary>
/// Het kader: wie er in het bestuur, de raad van elf of een eigen categorie zit, en hoe lang al (wens 2026-11-17,
/// eigen categorieën 2026-11-18). Hoort er een periode bij de categorie, dan duurt die drie jaar en kan hij met één of
/// twee jaar verlengd worden; die periodes staan er los in en lopen door (<see cref="BoardSeat.PeriodOn"/>).
///
/// Alles gaat met de hand: het volgt niet uit de rollen of uit het kader op de website, want de geschiedenis staat
/// nergens anders.
/// </summary>
public sealed class BoardAdministration(DrammersDbContext db, IAuditLogger audit, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    // ----- Categorieën --------------------------------------------------------------------------------------------

    /// <summary>De categorieën in de volgorde van het portal, met hoeveel plekken er in staan.</summary>
    public async Task<IReadOnlyList<BoardBodyView>> BodiesAsync(CancellationToken cancellationToken) =>
        await db.BoardBodies.AsNoTracking().OrderBy(b => b.Position).ThenBy(b => b.Name)
            .Select(b => new BoardBodyView(b.Id, b.Name, b.HasTerms, b.Position, db.BoardSeats.Count(s => s.BodyId == b.Id)))
            .ToListAsync(cancellationToken);

    public async Task<Guid> CreateBodyAsync(BoardBodyInput input, CancellationToken cancellationToken)
    {
        var body = new BoardBody { Id = IdGenerator.NewId(), Name = string.Empty };
        await ApplyBodyAsync(body, input, cancellationToken);
        db.BoardBodies.Add(body);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-body.created", "BoardBody", body.Id.ToString(), null,
            JsonSerializer.Serialize(new { body.Name, body.HasTerms }, Json)), cancellationToken);
        return body.Id;
    }

    public async Task UpdateBodyAsync(Guid id, BoardBodyInput input, CancellationToken cancellationToken)
    {
        var body = await FindBodyAsync(id, cancellationToken);
        var hadTerms = body.HasTerms;
        await ApplyBodyAsync(body, input, cancellationToken);
        if (hadTerms && !body.HasTerms)
        {
            // Zonder periodes hoort er ook geen periode meer bij de mensen in deze categorie.
            foreach (var seat in await db.BoardSeats.Where(s => s.BodyId == id).ToListAsync(cancellationToken))
            {
                seat.Terms = [];
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-body.updated", "BoardBody", id.ToString(), null,
            JsonSerializer.Serialize(new { body.Name, body.HasTerms, body.Position }, Json)), cancellationToken);
    }

    /// <summary>Kan alleen als er niemand meer in staat; anders zou de geschiedenis verdwijnen.</summary>
    public async Task DeleteBodyAsync(Guid id, CancellationToken cancellationToken)
    {
        var body = await FindBodyAsync(id, cancellationToken);
        if (await db.BoardSeats.AnyAsync(s => s.BodyId == id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.BoardBodyInUse,
                "Er staan nog mensen in deze categorie. Haal die er eerst uit of zet ze in een andere categorie.", DomainErrorKind.Conflict);
        }

        db.BoardBodies.Remove(body);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-body.deleted", "BoardBody", id.ToString(), null,
            JsonSerializer.Serialize(new { body.Name }, Json)), cancellationToken);
    }

    // ----- Plekken ------------------------------------------------------------------------------------------------

    /// <summary>Zonder <paramref name="includePast"/> alleen wie er nu in zit; altijd in volgorde van anciënniteit.</summary>
    public async Task<IReadOnlyList<BoardSeatView>> ListAsync(Guid? bodyId, bool includePast, CancellationToken cancellationToken)
    {
        var query = db.BoardSeats.AsNoTracking();
        if (bodyId is { } only)
        {
            query = query.Where(s => s.BodyId == only);
        }

        if (!includePast)
        {
            query = query.Where(s => s.EndDate == null);
        }

        var seats = await query.OrderBy(s => s.EndDate != null).ThenBy(s => s.StartDate).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        var bodies = await db.BoardBodies.AsNoTracking().ToDictionaryAsync(b => b.Id, cancellationToken);
        return [.. seats.Select(s => View(s, bodies[s.BodyId]))];
    }

    public async Task<Guid> CreateAsync(BoardSeatInput input, CancellationToken cancellationToken)
    {
        var seat = new BoardSeat { Id = IdGenerator.NewId(), Name = string.Empty };
        var body = await ApplyAsync(seat, input, cancellationToken);
        db.BoardSeats.Add(seat);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-seat.created", "BoardSeat", seat.Id.ToString(), null,
            JsonSerializer.Serialize(new { body = body.Name, seat.Name, seat.Function, seat.StartDate }, Json)), cancellationToken);
        return seat.Id;
    }

    public async Task UpdateAsync(Guid id, BoardSeatInput input, CancellationToken cancellationToken)
    {
        var seat = await FindAsync(id, cancellationToken);
        var body = await ApplyAsync(seat, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-seat.updated", "BoardSeat", id.ToString(), null,
            JsonSerializer.Serialize(new { body = body.Name, seat.Name, seat.Function, seat.StartDate, seat.EndDate }, Json)), cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var seat = await FindAsync(id, cancellationToken);
        db.BoardSeats.Remove(seat);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-seat.deleted", "BoardSeat", id.ToString(), null,
            JsonSerializer.Serialize(new { seat.Name }, Json)), cancellationToken);
    }

    /// <summary>De periode waarin iemand nu zit met één of twee jaar verlengen; de periode zelf blijft drie jaar.</summary>
    public async Task<BoardSeatView> ExtendAsync(Guid id, int years, string? note, CancellationToken cancellationToken)
    {
        var (seat, body) = await FindWithBodyAsync(id, cancellationToken);
        if (!body.HasTerms)
        {
            throw new DomainException(ErrorCodes.Validation, $"{body.Name} heeft geen periodes om te verlengen.");
        }

        if (years is < 1 or > BoardSeat.MaxExtensionYears)
        {
            throw new DomainException(ErrorCodes.Validation, $"Verlengen kan met 1 of {BoardSeat.MaxExtensionYears} jaar.");
        }

        var term = RecordPeriodsUpToNow(seat) ?? throw new DomainException(ErrorCodes.Validation, "Deze plek heeft nog geen periode.");
        term.ExtensionYears = years;
        term.Note = string.IsNullOrWhiteSpace(note) ? term.Note : note.Trim();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-seat.extended", "BoardSeat", id.ToString(), null,
            JsonSerializer.Serialize(new { years, term.StartDate, until = term.EndDate }, Json)), cancellationToken);
        return View(seat, body);
    }

    /// <summary>
    /// Een nieuwe periode van drie jaar, standaard aansluitend op de vorige (herverkozen). Een verlenging van de vorige
    /// periode blijft staan: die hoort bij de jaren die al zijn gemaakt.
    /// </summary>
    public async Task<BoardSeatView> RenewAsync(Guid id, DateOnly? startDate, string? note, CancellationToken cancellationToken)
    {
        var (seat, body) = await FindWithBodyAsync(id, cancellationToken);
        if (!body.HasTerms)
        {
            throw new DomainException(ErrorCodes.Validation, $"{body.Name} heeft geen periodes.");
        }

        RecordPeriodsUpToNow(seat);
        var start = startDate ?? seat.LastRecordedTerm?.EndDate ?? seat.StartDate;
        if (seat.LastRecordedTerm is { } current && start <= current.StartDate)
        {
            throw new DomainException(ErrorCodes.Validation, "Een nieuwe periode begint na de vorige.");
        }

        seat.Terms.Add(new BoardTerm { StartDate = start, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });
        if (seat.EndDate is not null)
        {
            // Opnieuw begonnen: dan zit hij er weer in.
            seat.EndDate = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-seat.renewed", "BoardSeat", id.ToString(), null,
            JsonSerializer.Serialize(new { start }, Json)), cancellationToken);
        return View(seat, body);
    }

    /// <summary>
    /// Legt de doorgerekende periodes vast tot en met die van vandaag en geeft de laatste terug. Dat gebeurt pas als
    /// iemand aan de periodes komt (verlengen of een nieuwe periode): dan moet er staan welke periodes er al waren.
    /// </summary>
    private BoardTerm? RecordPeriodsUpToNow(BoardSeat seat)
    {
        if (seat.PeriodOn(Today) is not { Recorded: false } period)
        {
            return seat.LastRecordedTerm;
        }

        for (var start = seat.LastRecordedTerm!.EndDate; start <= period.StartDate; start = start.AddYears(BoardSeat.TermYears))
        {
            seat.Terms.Add(new BoardTerm { StartDate = start });
        }

        return seat.LastRecordedTerm;
    }

    private BoardSeatView View(BoardSeat seat, BoardBody body)
    {
        var period = seat.PeriodOn(Today);
        var terms = seat.Terms.OrderBy(t => t.StartDate).ToList();
        return new BoardSeatView(seat.Id, body.Id, body.Name, body.HasTerms, seat.MemberId, seat.Name, seat.Function,
            seat.StartDate, seat.EndDate, seat.Notes, seat.DaysServed(Today), period?.Number, period?.StartDate, period?.EndDate,
            period is null ? null : period.EndDate.DayNumber - Today.DayNumber, period?.ExtensionYears ?? 0,
            [
                .. terms.Select((t, i) => new BoardTermView(i + 1, t.StartDate, t.EndDate, t.ExtensionYears, t.Note,
                    period is { Recorded: true } && t.StartDate == period.StartDate)),
            ]);
    }

    private async Task ApplyBodyAsync(BoardBody body, BoardBodyInput input, CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een naam voor de categorie in.");
        }

        if (await db.BoardBodies.AnyAsync(b => b.Id != body.Id && b.Name == name, cancellationToken))
        {
            throw new DomainException(ErrorCodes.BoardBodyNameTaken, "Er is al een categorie met deze naam.", DomainErrorKind.Conflict);
        }

        (body.Name, body.HasTerms, body.Position) = (name, input.HasTerms, input.Position);
    }

    private async Task<BoardBody> ApplyAsync(BoardSeat seat, BoardSeatInput input, CancellationToken cancellationToken)
    {
        if (input.EndDate is { } end && end < input.StartDate)
        {
            throw new DomainException(ErrorCodes.Validation, "De einddatum ligt vóór de begindatum.");
        }

        var body = await FindBodyAsync(input.BodyId, cancellationToken);
        var name = input.Name?.Trim();
        if (input.MemberId is { } memberId)
        {
            // De naam blijft gelijk aan de ledenlijst; zo hoef je een naamswijziging niet op twee plekken te doen.
            name = await db.Members.Where(m => m.Id == memberId).Select(m => m.FullName).SingleOrDefaultAsync(cancellationToken)
                ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een lid of vul een naam in.");
        }

        (seat.BodyId, seat.MemberId, seat.Name) = (body.Id, input.MemberId, name);
        seat.Function = string.IsNullOrWhiteSpace(input.Function) ? null : input.Function.Trim();
        (seat.StartDate, seat.EndDate) = (input.StartDate, input.EndDate);
        seat.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        seat.Terms = Terms(input, body, seat.StartDate);
        return body;
    }

    private static List<BoardTerm> Terms(BoardSeatInput input, BoardBody body, DateOnly seatStart)
    {
        if (!body.HasTerms)
        {
            // Zonder periodes een meegestuurde periode negeren in plaats van weigeren.
            return [];
        }

        List<BoardTermInput> given = [.. input.Terms];
        if (given.Count == 0)
        {
            // Een nieuwe bestuurder begint meteen aan zijn eerste periode van drie jaar; de volgende rekent
            // BoardSeat.PeriodOn door, zodat je niet elke periode met de hand hoeft op te voeren.
            given.Add(new BoardTermInput(seatStart, 0, null));
        }

        var terms = given.OrderBy(t => t.StartDate).ToList();
        if (terms[0].StartDate < seatStart)
        {
            throw new DomainException(ErrorCodes.Validation, "Een periode kan niet beginnen vóór de datum waarop iemand erin kwam.");
        }

        for (var i = 0; i < terms.Count; i++)
        {
            if (terms[i].ExtensionYears is < 0 or > BoardSeat.MaxExtensionYears)
            {
                throw new DomainException(ErrorCodes.Validation, $"Een periode kan met 0, 1 of {BoardSeat.MaxExtensionYears} jaar verlengd worden.");
            }

            if (i > 0 && terms[i].StartDate <= terms[i - 1].StartDate)
            {
                throw new DomainException(ErrorCodes.Validation, "Twee periodes kunnen niet op dezelfde dag beginnen.");
            }
        }

        return
        [
            .. terms.Select(t => new BoardTerm
            {
                StartDate = t.StartDate,
                ExtensionYears = t.ExtensionYears,
                Note = string.IsNullOrWhiteSpace(t.Note) ? null : t.Note.Trim(),
            }),
        ];
    }

    private async Task<BoardBody> FindBodyAsync(Guid id, CancellationToken cancellationToken) =>
        await db.BoardBodies.SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.BoardBodyNotFound, "Deze categorie bestaat niet (meer).", DomainErrorKind.NotFound);

    private async Task<BoardSeat> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.BoardSeats.SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.BoardSeatNotFound, "Deze plek bestaat niet (meer).", DomainErrorKind.NotFound);

    private async Task<(BoardSeat Seat, BoardBody Body)> FindWithBodyAsync(Guid id, CancellationToken cancellationToken)
    {
        var seat = await FindAsync(id, cancellationToken);
        return (seat, await FindBodyAsync(seat.BodyId, cancellationToken));
    }
}
