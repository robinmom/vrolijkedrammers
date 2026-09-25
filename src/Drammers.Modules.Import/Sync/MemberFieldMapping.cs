namespace Drammers.Modules.Import.Sync;

/// <summary>
/// Welk vrij veld van e-Boekhouden (<c>freeText1</c>..<c>freeText10</c>) welk ledengegeven bevat (B-06). Een leeg veld
/// betekent "niet gemapt": het gegeven wordt dan lokaal beheerd. Opgeslagen in <c>config.AppConfiguration</c>.
/// </summary>
public sealed record MemberFieldMapping(
    string? BirthDate,
    string? JoinYear,
    string? Status,
    string? Category,
    IReadOnlyList<string> InactiveStatusValues)
{
    public static readonly string[] FreeTextFields =
        ["freeText1", "freeText2", "freeText3", "freeText4", "freeText5", "freeText6", "freeText7", "freeText8", "freeText9", "freeText10"];

    /// <summary>Standaard: niets gemapt; statuswaarden die "niet meer actief" betekenen.</summary>
    public static MemberFieldMapping Default { get; } = new(null, null, null, null, ["opgezegd", "inactief", "geroyeerd", "overleden", "uitgeschreven"]);

    /// <summary>Foutmelding als de mapping ongeldig is (onbekend veld, of één veld voor twee gegevens); anders <c>null</c>.</summary>
    public string? Validate()
    {
        string?[] used = [BirthDate, JoinYear, Status, Category];
        foreach (var field in used.Where(f => f is not null))
        {
            if (!FreeTextFields.Contains(field))
            {
                return $"Onbekend vrij veld '{field}'; kies freeText1 t/m freeText10.";
            }
        }

        var mapped = used.Where(f => f is not null).ToList();
        return mapped.Count != mapped.Distinct().Count() ? "Elk vrij veld kan maar voor één gegeven gebruikt worden." : null;
    }
}
