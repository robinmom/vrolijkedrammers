namespace Drammers.Website.Pages;

/// <summary>Blauwe paginakop: kruimelpad (laatste zonder link), titel en een korte inleiding.</summary>
public sealed record PageBand(string Title, string? Lead, params (string? Href, string Label)[] Crumbs);
