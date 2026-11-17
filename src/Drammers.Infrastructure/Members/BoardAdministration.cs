using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Board;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

public sealed record BoardTermInput(DateOnly StartDate, int ExtensionYears, string? Note);

public sealed record BoardSeatInput(
    GoverningBody Body, Guid? MemberId, string Name, string? Function, DateOnly StartDate, DateOnly? EndDate, string? Notes,
    IReadOnlyList<BoardTermInput> Terms);

public sealed record BoardTermView(int Number, DateOnly StartDate, DateOnly EndDate, int ExtensionYears, string? Note, bool IsCurrent);

/// <summary>
/// Een plek in het bestuur of de raad van elf, met alles wat het portal nodig heeft om de balk te tekenen: hoeveel
/// dagen iemand er in zit en waar de periode van drie jaar eindigt.
/// </summary>
public sealed record BoardSeatView(
    Guid Id, GoverningBody Body, Guid? MemberId, string Name, string? Function, DateOnly StartDate, DateOnly? EndDate,
    string? Notes, int DaysServed, DateOnly? TermEnd, int? DaysUntilTermEnd, IReadOnlyList<BoardTermView> Terms);

/// <summary>
/// Wie er in het bestuur en de raad van elf zit, en hoe lang al (wens 2026-11-17). Het bestuur is verkiesbaar: een
/// periode duurt drie jaar en kan met één of twee jaar verlengd worden, dus die periodes staan er los in. De raad van
/// elf begint wanneer iemand begint en stopt wanneer hij zelf wil; daar is alleen het begin (en einde) bekend.
///
/// Alles gaat met de hand: het volgt niet uit de rollen of uit het kader op de website, want de geschiedenis staat
/// nergens anders.
/// </summary>
public sealed class BoardAdministration(DrammersDbContext db, IAuditLogger audit, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    /// <summary>Zonder <paramref name="includePast"/> alleen wie er nu in zit; altijd in volgorde van anciënniteit.</summary>
    public async Task<IReadOnlyList<BoardSeatView>> ListAsync(GoverningBody? body, bool includePast, CancellationToken cancellationToken)
    {
        var query = db.BoardSeats.AsNoTracking();
        if (body is { } only)
        {
            query = query.Where(s => s.Body == only);
        }

        if (!includePast)
        {
            query = query.Where(s => s.EndDate == null);
        }

        var seats = await query.OrderBy(s => s.EndDate != null).ThenBy(s => s.StartDate).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        return [.. seats.Select(View)];
    }

    public async Task<Guid> CreateAsync(BoardSeatInput input, CancellationToken cancellationToken)
    {
        var seat = new BoardSeat { Id = IdGenerator.NewId(), Name = string.Empty };
        await ApplyAsync(seat, input, cancellationToken);
        db.BoardSeats.Add(seat);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-seat.created", "BoardSeat", seat.Id.ToString(), null,
            JsonSerializer.Serialize(new { seat.Body, seat.Name, seat.Function, seat.StartDate }, Json)), cancellationToken);
        return seat.Id;
    }

    public async Task UpdateAsync(Guid id, BoardSeatInput input, CancellationToken cancellationToken)
    {
        var seat = await FindAsync(id, cancellationToken);
        await ApplyAsync(seat, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-seat.updated", "BoardSeat", id.ToString(), null,
            JsonSerializer.Serialize(new { seat.Body, seat.Name, seat.Function, seat.StartDate, seat.EndDate }, Json)), cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var seat = await FindAsync(id, cancellationToken);
        db.BoardSeats.Remove(seat);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-seat.deleted", "BoardSeat", id.ToString(), null,
            JsonSerializer.Serialize(new { seat.Name }, Json)), cancellationToken);
    }

    /// <summary>De huidige periode met één of twee jaar verlengen; de periode zelf blijft drie jaar.</summary>
    public async Task<BoardSeatView> ExtendAsync(Guid id, int years, string? note, CancellationToken cancellationToken)
    {
        var seat = await FindAsync(id, cancellationToken);
        if (seat.Body != GoverningBody.Board)
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen een bestuursperiode kan verlengd worden.");
        }

        var term = seat.CurrentTerm ?? throw new DomainException(ErrorCodes.Validation, "Deze bestuurder heeft nog geen periode.");
        if (years is < 1 or > BoardSeat.MaxExtensionYears)
        {
            throw new DomainException(ErrorCodes.Validation, $"Verlengen kan met 1 of {BoardSeat.MaxExtensionYears} jaar.");
        }

        term.ExtensionYears = years;
        term.Note = string.IsNullOrWhiteSpace(note) ? term.Note : note.Trim();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("board-seat.extended", "BoardSeat", id.ToString(), null,
            JsonSerializer.Serialize(new { years, term.StartDate, until = term.EndDate }, Json)), cancellationToken);
        return View(seat);
    }

    /// <summary>
    /// Een nieuwe bestuursperiode van drie jaar, standaard aansluitend op de vorige (herverkozen). Een verlenging van
    /// de vorige periode blijft staan: die hoort bij de jaren die al zijn gemaakt.
    /// </summary>
    public async Task<BoardSeatView> RenewAsync(Guid id, DateOnly? startDate, string? note, CancellationToken cancellationToken)
    {
        var seat = await FindAsync(id, cancellationToken);
        if (seat.Body != GoverningBody.Board)
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen het bestuur heeft periodes.");
        }

        var start = startDate ?? seat.CurrentTerm?.EndDate ?? seat.StartDate;
        if (seat.CurrentTerm is { } current && start <= current.StartDate)
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
        return View(seat);
    }

    private BoardSeatView View(BoardSeat seat)
    {
        var current = seat.CurrentTerm;
        var terms = seat.Terms.OrderBy(t => t.StartDate).ToList();
        return new BoardSeatView(seat.Id, seat.Body, seat.MemberId, seat.Name, seat.Function, seat.StartDate, seat.EndDate, seat.Notes,
            seat.DaysServed(Today), current?.EndDate, current is null ? null : current.EndDate.DayNumber - Today.DayNumber,
            [
                .. terms.Select((t, i) => new BoardTermView(i + 1, t.StartDate, t.EndDate, t.ExtensionYears, t.Note,
                    current is not null && t.StartDate == current.StartDate)),
            ]);
    }

    private async Task ApplyAsync(BoardSeat seat, BoardSeatInput input, CancellationToken cancellationToken)
    {
        if (input.EndDate is { } end && end < input.StartDate)
        {
            throw new DomainException(ErrorCodes.Validation, "De einddatum ligt vóór de begindatum.");
        }

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

        (seat.Body, seat.MemberId, seat.Name) = (input.Body, input.MemberId, name);
        seat.Function = string.IsNullOrWhiteSpace(input.Function) ? null : input.Function.Trim();
        (seat.StartDate, seat.EndDate) = (input.StartDate, input.EndDate);
        seat.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        seat.Terms = Terms(input, seat.Body, seat.StartDate);
    }

    private static List<BoardTerm> Terms(BoardSeatInput input, GoverningBody body, DateOnly seatStart)
    {
        if (body != GoverningBody.Board)
        {
            // De raad van elf heeft geen periodes; een meegestuurde periode negeren we in plaats van weigeren.
            return [];
        }

        List<BoardTermInput> given = [.. input.Terms];
        if (given.Count == 0)
        {
            // Een nieuwe bestuurder begint meteen aan zijn eerste periode van drie jaar.
            given.Add(new BoardTermInput(seatStart, 0, null));
        }

        var terms = given.OrderBy(t => t.StartDate).ToList();
        if (terms[0].StartDate < seatStart)
        {
            throw new DomainException(ErrorCodes.Validation, "Een periode kan niet beginnen vóór de datum waarop iemand in het bestuur kwam.");
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

    private async Task<BoardSeat> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.BoardSeats.SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.BoardSeatNotFound, "Deze plek bestaat niet (meer).", DomainErrorKind.NotFound);
}
