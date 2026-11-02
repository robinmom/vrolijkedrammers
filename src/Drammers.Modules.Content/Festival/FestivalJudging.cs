using System.Globalization;

namespace Drammers.Modules.Content.Festival;

/// <summary>
/// Punten van de jury: 0 tot en met 10 in stappen van een half punt (bevestigd door de organisatie op 2026-11-02; in de
/// praktijk geven juryleden meestal hele punten). Overal in het Nederlands geschreven en zonder nul achter de komma, dus
/// "7,5" en "8" (niet "8,0").
/// </summary>
public static class FestivalPoints
{
    public const int Max = 10;

    public const decimal Step = 0.5m;

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    public static bool IsValid(decimal value) => value >= 0m && value <= Max && value % Step == 0m;

    public static string Format(decimal value) => value.ToString("0.#", Dutch);
}

/// <summary>
/// De vier onderdelen van het juryformulier (2026-10-15), elk 0 tot en met 10 in halve punten (zie
/// <see cref="FestivalPoints"/>). Bij solo heet het eerste onderdeel Ritme in plaats van Synchroniteit, omdat er bij een
/// solodans niets te synchroniseren valt (bevestigd door de organisatie op 2026-11-02).
/// </summary>
public enum FestivalCriterion
{
    Synchronicity,
    Choreography,
    Execution,
    Difficulty,
}

/// <summary>Een jurylid van een festival met een vast nummer (Jurylid 1, 2, 3 in het rapport, zonder naam).</summary>
public sealed class FestivalJuror
{
    public Guid FestivalId { get; set; }

    public Guid UserId { get; set; }

    public int Number { get; set; }

    public DateTime AddedAt { get; set; }
}

/// <summary>Punten van één jurylid voor één onderdeel van een dans; de nieuwste invulling telt (net als bij de optocht).</summary>
public sealed class FestivalScore
{
    public Guid FestivalId { get; set; }

    public Guid EntryId { get; set; }

    public Guid UserId { get; set; }

    public FestivalCriterion Criterion { get; set; }

    /// <summary>0 tot en met 10 in halve punten; zie <see cref="FestivalPoints"/>.</summary>
    public decimal Value { get; set; }

    public DateTime ScoredAt { get; set; }

    public DateTime ReceivedAt { get; set; }
}

/// <summary>De opmerking van een jurylid bij een dans; komt anoniem in het rapport van de vereniging.</summary>
public sealed class FestivalComment
{
    public Guid FestivalId { get; set; }

    public Guid EntryId { get; set; }

    public Guid UserId { get; set; }

    public required string Text { get; set; }

    public DateTime WrittenAt { get; set; }
}

/// <summary>Een jurylid heeft een programmacategorie ingediend; daarna is daar niets meer aan te wijzigen.</summary>
public sealed class FestivalCategorySubmission
{
    public Guid FestivalId { get; set; }

    public required string CategoryKey { get; set; }

    public Guid UserId { get; set; }

    public DateTime SubmittedAt { get; set; }
}
