namespace Drammers.Modules.Membership.Members;

/// <summary>Voornaam, tussenvoegsel en achternaam (best effort, OQ-01).</summary>
public sealed record ParsedName(string? FirstName, string? NamePrefix, string? LastName);

/// <summary>
/// Splitst het ene naamveld van e-Boekhouden in naamdelen. Herkent "Piet van den Berg", "Berg, Piet van den" en
/// "P. de Vries". Onzekere gevallen (één woord, bedrijfsnaam) krijgen alleen een achternaam; het bestuur kan in het
/// portal corrigeren.
/// </summary>
public static class DutchNameParser
{
    private static readonly string[] Prefixes =
    [
        "van der", "van den", "van de", "van het", "van 't", "in 't", "in het", "in de", "op de", "op den", "op het",
        "uit de", "uit den", "uit het", "aan de", "aan den", "bij de", "de la", "van", "de", "den", "der", "het", "'t", "te", "ten", "ter", "la", "le",
    ];

    public static ParsedName Parse(string? fullName)
    {
        var name = string.Join(' ', (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (name.Length == 0)
        {
            return new ParsedName(null, null, null);
        }

        // "Achternaam, Voornaam tussenvoegsel"
        var comma = name.IndexOf(',', StringComparison.Ordinal);
        if (comma > 0)
        {
            var last = name[..comma].Trim();
            var rest = name[(comma + 1)..].Trim();
            var (first, prefix) = SplitTrailingPrefix(rest);
            return new ParsedName(NullIfEmpty(first), prefix, NullIfEmpty(last));
        }

        var words = name.Split(' ');
        if (words.Length == 1)
        {
            return new ParsedName(null, null, name);
        }

        // Voornaam = eerste woord (initialen "P.J." of "P. J." samen); daarna een eventueel tussenvoegsel.
        var firstCount = 1;
        while (firstCount < words.Length - 1 && IsInitial(words[firstCount - 1]) && IsInitial(words[firstCount]))
        {
            firstCount++;
        }

        var firstName = string.Join(' ', words[..firstCount]);
        var remainder = string.Join(' ', words[firstCount..]);
        foreach (var candidate in Prefixes)
        {
            if (remainder.StartsWith(candidate + " ", StringComparison.OrdinalIgnoreCase) && remainder.Length > candidate.Length + 1)
            {
                return new ParsedName(firstName, remainder[..candidate.Length].ToLowerInvariant(), remainder[(candidate.Length + 1)..]);
            }
        }

        return new ParsedName(firstName, null, remainder);
    }

    private static (string First, string? Prefix) SplitTrailingPrefix(string value)
    {
        foreach (var candidate in Prefixes)
        {
            if (value.EndsWith(" " + candidate, StringComparison.OrdinalIgnoreCase))
            {
                return (value[..^(candidate.Length + 1)].Trim(), candidate.ToLowerInvariant());
            }
        }

        return (value, null);
    }

    private static bool IsInitial(string word) => word.Length <= 4 && word.EndsWith('.');

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
