namespace Drammers.Website.Pages;

/// <summary>Blauwe paginakop: kruimelpad (laatste zonder link), titel en een korte inleiding.</summary>
public sealed record PageBand(string Title, string? Lead, params (string? Href, string Label)[] Crumbs);

/// <summary>
/// Knoppen naar het archief per carnavalsjaar (fase 21g) onder het nieuws- of fotooverzicht.
/// </summary>
/// <param name="Path">Bijvoorbeeld <c>/nieuws</c>.</param>
/// <param name="Query">Extra querystring zonder seizoen (bijvoorbeeld <c>soort=optocht</c>), of <c>null</c>.</param>
/// <param name="Current">Het actieve carnavalsjaar.</param>
/// <param name="Shown">Het jaar dat nu getoond wordt.</param>
/// <param name="Archive">De oudere jaren met inhoud, nieuwste eerst.</param>
public sealed record SeasonNav(string Path, string? Query, Infrastructure.Content.CarnivalSeason Current, Infrastructure.Content.CarnivalSeason Shown,
    IReadOnlyList<Infrastructure.Content.CarnivalSeason> Archive)
{
    public bool IsArchive => Shown.Slug != Current.Slug;

    public string Href(Infrastructure.Content.CarnivalSeason season)
    {
        var parts = new[] { Query, season.Slug == Current.Slug ? null : $"seizoen={season.Slug}" }.Where(p => p is not null).ToList();
        return parts.Count == 0 ? Path : $"{Path}?{string.Join('&', parts)}";
    }
}
