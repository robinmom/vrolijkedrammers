using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Drammers.SharedKernel.Errors;

namespace Drammers.Infrastructure.Content;

/// <summary>Webadressen (slugs) voor de website: kleine letters, zonder accenten, woorden met een streepje.</summary>
public static partial class Slugs
{
    public const int MaxLength = 100;

    public static string From(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        var slug = NonWord().Replace(builder.ToString().Replace("'", "", StringComparison.Ordinal), "-").Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug.Length == 0 ? "pagina" : slug;
    }

    /// <summary>Controleert een zelf gekozen webadres.</summary>
    public static string Validate(string slug)
    {
        if (!Valid().IsMatch(slug))
        {
            throw new DomainException(ErrorCodes.Validation,
                "Een webadres bestaat uit kleine letters, cijfers en streepjes (bijvoorbeeld over-ons).");
        }

        return slug;
    }

    /// <summary>Maakt een webadres uniek door -2, -3 … toe te voegen zolang <paramref name="taken"/> ja zegt.</summary>
    public static async Task<string> UniqueAsync(string slug, Func<string, Task<bool>> taken)
    {
        var candidate = slug;
        for (var i = 2; await taken(candidate); i++)
        {
            var suffix = $"-{i}";
            candidate = (slug.Length + suffix.Length > MaxLength ? slug[..(MaxLength - suffix.Length)] : slug) + suffix;
        }

        return candidate;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonWord();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Valid();
}

/// <summary>
/// Afbeeldingen die al vóór het opslaan zijn geüpload (fase 21a). Het portal uploadt eerst naar
/// <c>POST …/images</c> (typecontrole, virusscan, herschalen, zonder metadata) en stuurt het pad mee bij het opslaan.
/// </summary>
public static partial class UploadedImages
{
    public const string Folder = "uploads";

    /// <summary>
    /// Bepaalt het nieuwe afbeeldingspad: <c>null</c> = ongewijzigd, lege tekst = verwijderen, anders een pad uit de
    /// upload-map. Geeft ook het oude pad terug dat na het opslaan weg mag.
    /// </summary>
    public static (string? Path, string? Obsolete) Resolve(string? current, string? requested)
    {
        if (requested is null || requested == current)
        {
            return (current, null);
        }

        if (requested.Length == 0)
        {
            return (null, current);
        }

        if (!UploadPath().IsMatch(requested))
        {
            throw new DomainException(ErrorCodes.Validation, "Deze afbeelding is niet via het portal geüpload.");
        }

        return (requested, current);
    }

    [GeneratedRegex("^uploads/[0-9a-f]{32}\\.jpg$")]
    private static partial Regex UploadPath();
}
