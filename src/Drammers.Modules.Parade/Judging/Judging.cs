using Drammers.Modules.Parade.Categories;

namespace Drammers.Modules.Parade.Judging;

/// <summary>De vier criteria waarop de jury beoordeelt (fase 22), elk van 0 tot en met 100.</summary>
public enum JudgingCriterion
{
    Originality,
    Carnivalesque,
    Quality,
    Overall,
}

/// <summary>
/// Jury-instellingen van een categorie binnen één optocht (fase 22a): of de categorie beoordeeld wordt en hoe vaak elk
/// criterium meetelt. Een nieuwe optocht neemt deze instellingen over van de vorige.
/// </summary>
public sealed class ParadeJudgingCategory
{
    public Guid ParadeId { get; set; }

    public int CategoryId { get; set; }

    public bool Judged { get; set; } = true;

    public int WeightOriginality { get; set; } = 1;

    public int WeightCarnivalesque { get; set; } = 1;

    public int WeightQuality { get; set; } = 1;

    public int WeightOverall { get; set; } = 1;

    public const int MaxWeight = 5;

    /// <summary>Standaard zoals de uitslag-Excel van 2026: bij wagens telt kwaliteit dubbel, bij loopgroepen de algemene indruk.</summary>
    public static ParadeJudgingCategory Default(Guid paradeId, ParadeCategory category)
    {
        var vehicle = category.Type is CategoryType.TowedFloat or CategoryType.SelfPropelled or CategoryType.TowedOrSelfPropelled;
        return new ParadeJudgingCategory
        {
            ParadeId = paradeId,
            CategoryId = category.Id,
            WeightQuality = vehicle ? 2 : 1,
            WeightOverall = vehicle ? 1 : 2,
        };
    }

    public int WeightOf(JudgingCriterion criterion) => criterion switch
    {
        JudgingCriterion.Originality => WeightOriginality,
        JudgingCriterion.Carnivalesque => WeightCarnivalesque,
        JudgingCriterion.Quality => WeightQuality,
        _ => WeightOverall,
    };
}

/// <summary>Een jurylid beoordeelt binnen een optocht deze categorie; een categorie heeft meerdere juryleden.</summary>
public sealed class ParadeJurorAssignment
{
    public Guid ParadeId { get; set; }

    public Guid UserId { get; set; }

    public int CategoryId { get; set; }

    public DateTime AssignedAt { get; set; }
}

/// <summary>
/// Eén score: een jurylid, een inzending, een passage (voorbijtrekken 1–3) en een criterium, 0 tot en met 100 (fase 22b).
/// De app bewaart scores eerst op de telefoon en stuurt ze later; de nieuwste wijziging (tijd op het toestel) wint.
/// </summary>
public sealed class JudgingScore
{
    public Guid ParadeId { get; set; }

    public Guid RegistrationId { get; set; }

    public Guid UserId { get; set; }

    public int Pass { get; set; }

    public JudgingCriterion Criterion { get; set; }

    public int Value { get; set; }

    /// <summary>Moment van invullen op het toestel (UTC); bepaalt welke versie wint bij later versturen.</summary>
    public DateTime ScoredAt { get; set; }

    public DateTime ReceivedAt { get; set; }

    public const int Passes = 3;
}

/// <summary>
/// Een aantekening van een jurylid bij één inzending in één passage (fase 22b, wens 2026-11-05). Bedoeld voor het
/// jurylid zelf: bij het indienen leest hij zijn aantekeningen van alle drie de passages terug voordat hij zijn
/// oordeel vastzet.
///
/// Deze tekst wordt <b>niet gedeeld</b>: hij staat niet in de uitslag en niet in een rapport dat naar buiten gaat.
/// Het bestuur kan hem in het portal inzien, zodat een groep die erom vraagt alsnog antwoord kan krijgen.
///
/// Per passage en niet per inzending, omdat het jurylid de wagen drie keer ziet langskomen en per keer iets anders
/// opvalt. Net als bij een score wint de nieuwste wijziging volgens de klok van het toestel.
/// </summary>
public sealed class JudgingComment
{
    public Guid ParadeId { get; set; }

    public Guid RegistrationId { get; set; }

    public Guid UserId { get; set; }

    public int Pass { get; set; }

    public required string Text { get; set; }

    /// <summary>Moment van schrijven op het toestel (UTC); bepaalt welke versie wint bij later versturen.</summary>
    public DateTime WrittenAt { get; set; }

    public DateTime ReceivedAt { get; set; }

    public const int MaxLength = 1000;
}

/// <summary>Een jurylid heeft voor deze optocht ingediend; daarna kan hij niets meer wijzigen.</summary>
public sealed class JudgingSubmission
{
    public Guid ParadeId { get; set; }

    public Guid UserId { get; set; }

    public DateTime SubmittedAt { get; set; }
}

public enum OutsideDecision
{
    Approved,
    Rejected,
}

/// <summary>
/// Beslissing van het bestuur of de hoofdjury over de beoordeling van een inzending buiten de categorieën van het
/// jurylid (via "Hele optocht"). Zonder beslissing telt die beoordeling niet mee.
/// </summary>
public sealed class JudgingOutsideReview
{
    public Guid ParadeId { get; set; }

    public Guid UserId { get; set; }

    public Guid RegistrationId { get; set; }

    public OutsideDecision Decision { get; set; }

    public Guid? DecidedBy { get; set; }

    public DateTime DecidedAt { get; set; }
}
