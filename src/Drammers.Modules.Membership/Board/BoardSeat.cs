using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Membership.Board;

public enum GoverningBody
{
    /// <summary>Het bestuur: verkiesbaar, met periodes van drie jaar.</summary>
    Board,

    /// <summary>De raad van elf: begint wanneer iemand begint en stopt wanneer hij zelf wil, dus zonder periodes.</summary>
    CouncilOfEleven,
}

/// <summary>
/// Wie er in het bestuur of de raad van elf zit, met hoe lang al. Het bestuur is verkiesbaar: een bestuursperiode
/// duurt drie jaar en kan met één of twee jaar verlengd worden, dus die periodes staan er los in
/// (<see cref="Terms"/>). De raad van elf heeft geen periode: daar zijn alleen het begin en (als het zover is) het
/// einde bekend.
///
/// Dit wordt met de hand bijgehouden: de geschiedenis staat nergens anders en volgt niet uit de rollen of het kader
/// op de website.
/// </summary>
public sealed class BoardSeat : IAuditable
{
    /// <summary>Een bestuursperiode duurt drie jaar.</summary>
    public const int TermYears = 3;

    /// <summary>Een periode kan met één of twee jaar verlengd worden.</summary>
    public const int MaxExtensionYears = 2;

    public Guid Id { get; set; }

    public GoverningBody Body { get; set; }

    /// <summary>Het lid uit de ledenlijst; leeg bij iemand van lang geleden die er niet (meer) in staat.</summary>
    public Guid? MemberId { get; set; }

    public required string Name { get; set; }

    /// <summary>Bijvoorbeeld "Voorzitter", "Secretaris" of "Vorst"; leeg bij een gewoon lid.</summary>
    public string? Function { get; set; }

    /// <summary>In dit orgaan sinds; hier begint de balk met het aantal jaren.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Gestopt op; leeg zolang iemand er nog in zit.</summary>
    public DateOnly? EndDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>De bestuursperiodes, oudste eerst; leeg bij de raad van elf.</summary>
    public List<BoardTerm> Terms { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>De periode waar iemand nu in zit (de laatste); leeg bij de raad van elf.</summary>
    public BoardTerm? CurrentTerm => Terms.OrderBy(t => t.StartDate).LastOrDefault();

    /// <summary>Hoeveel dagen iemand er op <paramref name="on"/> in zit (of zat).</summary>
    public int DaysServed(DateOnly on) => Math.Max(0, (EndDate ?? on).DayNumber - StartDate.DayNumber);
}

/// <summary>Eén bestuursperiode van drie jaar, eventueel met één of twee jaar verlenging.</summary>
public sealed class BoardTerm
{
    public DateOnly StartDate { get; set; }

    /// <summary>Verlengd met 0, 1 of 2 jaar.</summary>
    public int ExtensionYears { get; set; }

    /// <summary>Bijvoorbeeld "verlengd tot na het jubileum".</summary>
    public string? Note { get; set; }

    public DateOnly EndDate => StartDate.AddYears(BoardSeat.TermYears + ExtensionYears);
}
