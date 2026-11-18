using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Membership.Board;

/// <summary>
/// Een categorie in het kader: bestuur, raad van elf, of een eigen categorie die de vereniging zelf toevoegt
/// (wens 18-11-2026). Of er periodes van drie jaar bij horen staat per categorie: het bestuur is verkiesbaar, de raad
/// van elf begint en stopt wanneer iemand dat zelf wil.
/// </summary>
public sealed class BoardBody : IAuditable
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Periodes van drie jaar, verlengbaar met één of twee jaar.</summary>
    public bool HasTerms { get; set; }

    /// <summary>Volgorde in het portal; bij een gelijke stand op naam.</summary>
    public int Position { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>De periode waarin iemand zit: vastgelegd of doorgerekend, zie <see cref="BoardSeat.PeriodOn"/>.</summary>
public sealed record BoardPeriod(int Number, DateOnly StartDate, DateOnly EndDate, int ExtensionYears, bool Recorded);

/// <summary>
/// Wie er in een categorie van het kader zit, met hoe lang al. Hoort er een periode bij (<see cref="BoardBody.HasTerms"/>),
/// dan duurt die drie jaar en kan hij met één of twee jaar verlengd worden; die periodes staan er los in
/// (<see cref="Terms"/>). Zonder periodes zijn alleen het begin en (als het zover is) het einde bekend.
///
/// Dit wordt met de hand bijgehouden: de geschiedenis staat nergens anders en volgt niet uit de rollen of het kader
/// op de website.
/// </summary>
public sealed class BoardSeat : IAuditable
{
    /// <summary>Een periode duurt drie jaar.</summary>
    public const int TermYears = 3;

    /// <summary>Een periode kan met één of twee jaar verlengd worden.</summary>
    public const int MaxExtensionYears = 2;

    public Guid Id { get; set; }

    /// <summary>De categorie in het kader (<see cref="BoardBody"/>).</summary>
    public Guid BodyId { get; set; }

    /// <summary>Het lid uit de ledenlijst; leeg bij iemand van lang geleden die er niet (meer) in staat.</summary>
    public Guid? MemberId { get; set; }

    public required string Name { get; set; }

    /// <summary>Bijvoorbeeld "Voorzitter", "Secretaris" of "Vorst"; leeg bij een gewoon lid.</summary>
    public string? Function { get; set; }

    /// <summary>In deze categorie sinds; hier begint de balk met het aantal jaren.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Gestopt op; leeg zolang iemand er nog in zit.</summary>
    public DateOnly? EndDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>De vastgelegde periodes, oudste eerst; leeg bij een categorie zonder periodes.</summary>
    public List<BoardTerm> Terms { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>De laatst vastgelegde periode; leeg bij een categorie zonder periodes.</summary>
    public BoardTerm? LastRecordedTerm => Terms.OrderBy(t => t.StartDate).LastOrDefault();

    /// <summary>Hoeveel dagen iemand er op <paramref name="on"/> in zit (of zat).</summary>
    public int DaysServed(DateOnly on) => Math.Max(0, (EndDate ?? on).DayNumber - StartDate.DayNumber);

    /// <summary>
    /// De periode waarin iemand op <paramref name="on"/> zit. De periodes lopen door: na de laatst vastgelegde periode
    /// volgt elke drie jaar een volgende, zonder verlenging. Zo staat het einde van de periode bij iemand die er al
    /// tien jaar in zit niet nog op zijn eerste drie jaar. Wie gestopt is, houdt de periode waarin dat gebeurde.
    /// </summary>
    public BoardPeriod? PeriodOn(DateOnly on)
    {
        if (LastRecordedTerm is not { } last)
        {
            return null;
        }

        var until = EndDate is { } stopped && stopped < on ? stopped : on;
        var (number, start, end, extension) = (Terms.Count, last.StartDate, last.EndDate, last.ExtensionYears);
        while (end <= until)
        {
            (number, start, end, extension) = (number + 1, end, end.AddYears(TermYears), 0);
        }

        return new BoardPeriod(number, start, end, extension, start == last.StartDate);
    }
}

/// <summary>Eén periode van drie jaar, eventueel met één of twee jaar verlenging.</summary>
public sealed class BoardTerm
{
    public DateOnly StartDate { get; set; }

    /// <summary>Verlengd met 0, 1 of 2 jaar.</summary>
    public int ExtensionYears { get; set; }

    /// <summary>Bijvoorbeeld "verlengd tot na het jubileum".</summary>
    public string? Note { get; set; }

    public DateOnly EndDate => StartDate.AddYears(BoardSeat.TermYears + ExtensionYears);
}
