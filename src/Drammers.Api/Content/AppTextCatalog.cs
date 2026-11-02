using System.Reflection;
using System.Text.Json;

namespace Drammers.Api.Content;

/// <summary>Een plek in de app-bron waar een tekst staat; <paramref name="Where"/> is bijv. "tekst" of "label".</summary>
public sealed record AppTextPlace(string File, string Where);

/// <summary>Eén zichtbare tekst uit de app, met de plekken waar hij staat.</summary>
public sealed record AppTextCatalogEntry(string Key, string Text, IReadOnlyList<AppTextPlace> Places);

/// <summary>
/// De catalogus van zichtbare teksten in de app: welke teksten bestaan er en waar staan ze. Hij wordt gegenereerd uit
/// de app-bron (<c>apps/mobile/scripts/collect-texts.mjs</c>) en gaat als embedded resource mee in deze build; CI
/// controleert of hij bij de bron past. Het portal gebruikt hem om de teksten te laten zien die aangepast kunnen
/// worden; de app zelf heeft hem niet nodig, want die zoekt een vervanging op met de tekst zelf.
/// </summary>
public sealed class AppTextCatalog
{
    private const string ResourceName = "Drammers.Api.Content.app-texts.catalog.json";

    private readonly Lazy<IReadOnlyDictionary<string, AppTextCatalogEntry>> entries = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public IEnumerable<AppTextCatalogEntry> Entries => entries.Value.Values;

    public AppTextCatalogEntry? Find(string key) => entries.Value.GetValueOrDefault(key);

    private static IReadOnlyDictionary<string, AppTextCatalogEntry> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"De tekstcatalogus ({ResourceName}) zit niet in deze build.");
        var document = JsonSerializer.Deserialize<CatalogDocument>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("De tekstcatalogus is leeg.");
        return document.Entries.ToDictionary(e => e.Key);
    }

    private sealed record CatalogDocument(int Version, IReadOnlyList<AppTextCatalogEntry> Entries);
}
