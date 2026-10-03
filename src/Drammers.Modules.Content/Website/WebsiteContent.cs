using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Content.Website;

/// <summary>
/// Instellingen van de website (fase 21a): één rij. De hero (foto, bovenregel, titel, ondertitel en twee knoppen) staat
/// bovenaan de website; titel, ondertitel en foto ook op het beginscherm van de app.
/// </summary>
public sealed class WebsiteSettings : IAuditable
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string? HeroEyebrow { get; set; }

    public required string HeroTitle { get; set; }

    public string? HeroSubtitle { get; set; }

    public string? HeroPrimaryLabel { get; set; }

    public WebsiteLink? HeroPrimaryLink { get; set; }

    public string? HeroSecondaryLabel { get; set; }

    public WebsiteLink? HeroSecondaryLink { get; set; }

    public string? HeroImageBlobPath { get; set; }

    /// <summary>Adres van de Facebookpagina (voor de knop "Naar onze pagina" en de feed op de homepage).</summary>
    public string? FacebookPageUrl { get; set; }

    public string? InstagramUrl { get; set; }

    /// <summary>De pagina Jeugdprinsen is pas zichtbaar als het bestuur hem aanzet (als hij gevuld is).</summary>
    public bool ShowYouthPrinces { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Vaste bestemmingen voor de knoppen in de hero.</summary>
public enum WebsiteLink
{
    Agenda,
    News,
    Photos,
    Parade,
    ParadeRegistration,
    Membership,
    Tickets,
    App,
    Contact,
}

/// <summary>Het menu waarin een pagina staat.</summary>
public enum WebsiteMenu
{
    /// <summary>Niet in een menu; alleen via een link bereikbaar.</summary>
    None,
    Association,
    Carnival,
}

/// <summary>Vaste tekstpagina van de website (Over ons, Ontstaan, Loillands …), in Markdown.</summary>
public sealed class WebsitePage : IAuditable
{
    public Guid Id { get; set; }

    /// <summary>Webadres onder de site, bijvoorbeeld <c>over-ons</c>.</summary>
    public required string Slug { get; set; }

    public required string Title { get; set; }

    public string? Intro { get; set; }

    public required string Body { get; set; }

    public string? ImageBlobPath { get; set; }

    public bool IsPublished { get; set; }

    /// <summary>Het menu waarin de pagina staat; binnen het menu op <see cref="SortOrder"/>.</summary>
    public WebsiteMenu Menu { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Fotoalbum dat onder de tekst staat (bijvoorbeeld bij de pagina's onder Carnaval).</summary>
    public Guid? PhotoAlbumId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Commissie van het kader (Bestuur, Raad van Elf, Convent, Leiding dansgarde …).</summary>
public sealed class Committee
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// Kaderlid op de website. Meestal gekozen uit de ledenlijst (<see cref="MemberId"/>); functie en pasfoto staan alleen
/// hier, niet in e-Boekhouden.
/// </summary>
public sealed class CommitteeMember : IAuditable
{
    public Guid Id { get; set; }

    public int CommitteeId { get; set; }

    public Guid? MemberId { get; set; }

    public required string Name { get; set; }

    public string? Function { get; set; }

    public string? PhotoBlobPath { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

public enum PrinceKind
{
    Prince,
    YouthPrince,
}

/// <summary>Prins of jeugdprins(es) in de prinsengalerie; niet gekoppeld aan de ledenlijst.</summary>
public sealed class Prince : IAuditable
{
    public Guid Id { get; set; }

    public PrinceKind Kind { get; set; }

    /// <summary>Het carnavalsjaar (het jaar van carnaval, bijvoorbeeld 2026).</summary>
    public int Year { get; set; }

    /// <summary>Bijvoorbeeld "Prins Ferry I".</summary>
    public required string PrinceName { get; set; }

    public string? Name { get; set; }

    public string? Motto { get; set; }

    public string? PhotoBlobPath { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

public enum AwardType
{
    /// <summary>'t Drammertje.</summary>
    Drammertje,

    /// <summary>De Verdienstelijke Didammer.</summary>
    VerdienstelijkeDidammer,

    /// <summary>Het Eikenloof van Boschslag.</summary>
    EikenloofVanBoschslag,
}

/// <summary>Onderscheiding: per jaar, met ontvanger, tekst (Markdown) en foto.</summary>
public sealed class Award : IAuditable
{
    public Guid Id { get; set; }

    public AwardType Type { get; set; }

    public int Year { get; set; }

    public required string Recipient { get; set; }

    public string? Body { get; set; }

    public string? PhotoBlobPath { get; set; }

    public required string Slug { get; set; }

    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
