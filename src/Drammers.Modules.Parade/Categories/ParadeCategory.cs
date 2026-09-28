namespace Drammers.Modules.Parade.Categories;

public enum AgeGroup
{
    Adult,
    Youth,
}

public enum CategoryType
{
    TowedFloat,
    SelfPropelled,
    TowedOrSelfPropelled,
    WalkingGroupLarge,
    WalkingGroupSmall,
    IndividualDuo,
}

/// <summary>Welke deelnemers tellen voor de grenzen (OQ-10: alleen de doelgroep).</summary>
public enum ParticipantCountBasis
{
    Total,
    ChildrenOnly,
    AdultsOnly,
}

public enum ValidationMode
{
    Block,
    Warn,
    None,
}

/// <summary>Categorie van een optochtinschrijving (docs/14 §3). Globaal (<see cref="ParadeId"/> leeg) of per optocht.</summary>
public sealed class ParadeCategory
{
    public int Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public AgeGroup AgeGroup { get; set; }

    public CategoryType Type { get; set; }

    public int? MinimumParticipants { get; set; }

    public int? MaximumParticipants { get; set; }

    public ParticipantCountBasis ParticipantCountBasis { get; set; }

    public ValidationMode ValidationMode { get; set; }

    public bool HasVehicle { get; set; }

    public bool Active { get; set; } = true;

    public int SortOrder { get; set; }

    public Guid? ParadeId { get; set; }

    public int CountOf(int children, int adults) => ParticipantCountBasis switch
    {
        ParticipantCountBasis.ChildrenOnly => children,
        ParticipantCountBasis.AdultsOnly => adults,
        _ => children + adults,
    };

    /// <summary>Startset (docs/14 §3), met de besluiten OQ-10 (alleen de doelgroep telt) en OQ-12 (10 = groot).</summary>
    public static IReadOnlyList<ParadeCategory> Seed =>
    [
        Create(1, 10, "ADULT_TOWED", "Volwassenen Getrokken wagens", AgeGroup.Adult, CategoryType.TowedFloat, 1, null, true, ValidationMode.Warn),
        Create(2, 20, "ADULT_SELF", "Volwassenen Zelfrijdende voertuigen", AgeGroup.Adult, CategoryType.SelfPropelled, 1, null, true, ValidationMode.Warn),
        Create(3, 30, "ADULT_WALK_L", "Volwassenen Loopgroepen groot (10+)", AgeGroup.Adult, CategoryType.WalkingGroupLarge, 10, null, false, ValidationMode.Block),
        Create(4, 40, "ADULT_WALK_S", "Volwassenen Loopgroepen klein (3-9)", AgeGroup.Adult, CategoryType.WalkingGroupSmall, 3, 9, false, ValidationMode.Block),
        Create(5, 50, "ADULT_INDIV", "Volwassenen Individueel of duo (1-2)", AgeGroup.Adult, CategoryType.IndividualDuo, 1, 2, false, ValidationMode.Block),
        Create(6, 60, "YOUTH_FLOAT", "Jeugd Getrokken en zelfrijdende wagens", AgeGroup.Youth, CategoryType.TowedOrSelfPropelled, 1, null, true, ValidationMode.Warn),
        Create(7, 70, "YOUTH_WALK_L", "Jeugd Loopgroepen groot (10+)", AgeGroup.Youth, CategoryType.WalkingGroupLarge, 10, null, false, ValidationMode.Block),
        Create(8, 80, "YOUTH_WALK_S", "Jeugd Loopgroepen klein (3-9)", AgeGroup.Youth, CategoryType.WalkingGroupSmall, 3, 9, false, ValidationMode.Block),
        Create(9, 90, "YOUTH_INDIV", "Jeugd Individueel of duo (1-2)", AgeGroup.Youth, CategoryType.IndividualDuo, 1, 2, false, ValidationMode.Block),
    ];

    private static ParadeCategory Create(
        int id, int sort, string code, string name, AgeGroup ageGroup, CategoryType type, int? min, int? max, bool vehicle, ValidationMode mode) => new()
        {
            Id = id,
            SortOrder = sort,
            Code = code,
            Name = name,
            AgeGroup = ageGroup,
            Type = type,
            MinimumParticipants = min,
            MaximumParticipants = max,
            HasVehicle = vehicle,
            ValidationMode = mode,
            ParticipantCountBasis = ageGroup == AgeGroup.Youth ? ParticipantCountBasis.ChildrenOnly : ParticipantCountBasis.AdultsOnly,
        };
}
