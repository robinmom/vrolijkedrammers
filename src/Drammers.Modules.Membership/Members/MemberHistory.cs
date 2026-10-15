namespace Drammers.Modules.Membership.Members;

public enum MemberExitReason
{
    Inactive,
    Deceased,
    Suspended,

    /// <summary>Volledig verwijderd (uitsluitlijst).</summary>
    Removed,
}

/// <summary>
/// Een uitschrijving (2026-10-15): vastgelegd zodra een lid niet meer actief is (inactief, overleden, geschorst) of volledig
/// is verwijderd. Op lidnummer, zodat het ook na verwijderen telt. Telt in het carnavalsjaar van de datum.
/// </summary>
public sealed class MemberExit
{
    public Guid Id { get; set; }

    public required string MemberNumber { get; set; }

    public MemberExitReason Reason { get; set; }

    public DateOnly ExitedOn { get; set; }

    /// <summary>Carnavalsjaar (het jaar van het eerstvolgende carnaval op <see cref="ExitedOn"/>).</summary>
    public int CarnivalYear { get; set; }
}

/// <summary>
/// Ledenverloop per carnavalsjaar: het totaal dat het systeem vastlegt (<see cref="SnapshotTotal"/>, het aantal actieve
/// leden aan het eind van het jaar) en cijfers die het bestuur zelf invult of importeert voor oudere jaren (gaan voor).
/// </summary>
public sealed class MemberYearStat
{
    public int CarnivalYear { get; set; }

    public int? SnapshotTotal { get; set; }

    public DateTime? SnapshotAt { get; set; }

    public int? ManualNew { get; set; }

    public int? ManualLeft { get; set; }

    public int? ManualTotal { get; set; }
}
