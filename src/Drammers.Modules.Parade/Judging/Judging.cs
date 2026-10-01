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
