namespace Drammers.Modules.Content.Website;

/// <summary>Soort bron-item bij het overzetten van de oude WordPress-site (fase 21e).</summary>
public enum WebsiteImportKind
{
    Post,
    Page,
    Gallery,
    GalleryPhoto,
    Prince,
    YouthPrince,
    Award,
    Kader,

    /// <summary>Pagina uit het menu Carnaval van de oude site: een pagina onder Carnaval met een fotoalbum eronder.</summary>
    CarnivalPage,
}

public enum WebsiteImportStatus
{
    Pending,
    Done,
    Skipped,
    Failed,
}

/// <summary>
/// Werklijst van de import (fase 21e): elk bron-item één keer, zodat de import te hervatten is en niets dubbel wordt
/// overgezet. <see cref="SourceKey"/> is uniek per soort (bijvoorbeeld het WordPress-id of het adres).
/// </summary>
public sealed class WebsiteImportItem
{
    public long Id { get; set; }

    public WebsiteImportKind Kind { get; set; }

    public required string SourceKey { get; set; }

    public string? SourceUrl { get; set; }

    public string? Title { get; set; }

    /// <summary>Gegevens uit de bron (JSON), zoals de tekst van een bericht of de gegevens van een prins.</summary>
    public string? Payload { get; set; }

    public WebsiteImportStatus Status { get; set; }

    /// <summary>Id van wat er in de nieuwe site van is gemaakt (nieuwsbericht, album, prins …).</summary>
    public string? TargetId { get; set; }

    public string? Error { get; set; }

    public int Attempts { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}

/// <summary>Permanente doorverwijzing van een oud adres (WordPress) naar de nieuwe website.</summary>
public sealed class WebsiteRedirect
{
    /// <summary>Oud pad, in kleine letters, zonder slash aan het eind (bijvoorbeeld <c>/optoch-2026</c>).</summary>
    public required string FromPath { get; set; }

    public required string ToPath { get; set; }

    public DateTime CreatedAt { get; set; }
}
