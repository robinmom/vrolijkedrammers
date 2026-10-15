namespace Drammers.Modules.Content.Festival;

public enum FestivalProgramItemKind
{
    /// <summary>Een programmacategorie: leeftijdscategorie + soort dans (bijv. Junioren Duo Karakter).</summary>
    Category,

    /// <summary>Opening, pauze, prijsuitreiking of iets anders zonder dansen.</summary>
    Block,
}

/// <summary>
/// Eén onderdeel van het programma van het Dansgardefestival (2026-10-15). De starttijd is leeg zolang het onderdeel
/// aansluit op het vorige; met een tijd begint het precies dan (gat of uitloop). Bij een categorie is <see cref="Minutes"/>
/// de tijd per dans, bij een blok de duur.
/// </summary>
public sealed class DanceFestivalProgramItem
{
    public Guid Id { get; set; }

    public Guid FestivalId { get; set; }

    public int SortOrder { get; set; }

    public FestivalProgramItemKind Kind { get; set; }

    /// <summary>Alleen bij een categorie: zie <see cref="FestivalCategories.KeyOf"/>.</summary>
    public string? CategoryKey { get; set; }

    /// <summary>Alleen bij een blok, bijv. "Pauze".</summary>
    public string? Title { get; set; }

    public TimeOnly? StartTime { get; set; }

    public int Minutes { get; set; }
}

/// <summary>
/// Programmacategorieën (2026-10-15): een categorie is leeftijdscategorie + soort dans, zoals in de uitslag-Excel van 2026
/// ("Junioren Garde", "Junioren Solo Garde", "Junioren Duo Karakter", "Jeugd Modern").
/// </summary>
public static class FestivalCategories
{
    public static string KeyOf(DanceFestivalEntry entry) =>
        $"{entry.Category}|{entry.DanceType}|{entry.Format}|{(entry.DanceType == DanceType.Show ? entry.ShowStyle?.ToString() : null)}";

    public static string NameOf(string key)
    {
        var parts = key.Split('|');
        var category = parts[0];
        var format = parts[2] switch { nameof(DanceFormat.Solo) => "Solo ", nameof(DanceFormat.Duo) => "Duo ", _ => "" };
        var kind = parts[1] == nameof(DanceType.Garde) ? "Garde" : parts[3] == nameof(ShowStyle.Modern) ? "Modern" : "Karakter";
        return $"{category} {format}{kind}";
    }

    /// <summary>Solo (de jury beoordeelt dan Ritme in plaats van Synchroniteit, zoals in de uitslag-Excel).</summary>
    public static bool IsSolo(string key) => key.Split('|')[2] == nameof(DanceFormat.Solo);

    /// <summary>
    /// Standaardvolgorde zoals het programma van 2026: eerst de gardedansen (groepen, dan solo, dan duo, elk van jong naar
    /// oud), daarna de showdansen (solo en duo, dan per leeftijd karakter en modern).
    /// </summary>
    public static int DefaultRank(string key)
    {
        var parts = key.Split('|');
        var age = Enum.TryParse<DanceCategory>(parts[0], out var c) ? (int)c : 9;
        var format = parts[2] switch { nameof(DanceFormat.Group) => 0, nameof(DanceFormat.Solo) => 1, _ => 2 };
        if (parts[1] == nameof(DanceType.Garde))
        {
            return (format * 10) + age;
        }

        return format == 0 ? 200 + (age * 2) + (parts[3] == nameof(ShowStyle.Modern) ? 1 : 0) : 100 + ((2 - format) * 10) + age;
    }
}
