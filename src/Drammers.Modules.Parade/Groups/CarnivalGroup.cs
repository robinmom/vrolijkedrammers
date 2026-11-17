using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Parade.Groups;

/// <summary>
/// Een carnavalsgroep van het dorp: wagenbouwers, loopgroepen en individuele deelnemers. De groep blijft bestaan
/// tussen de jaren, de inschrijving niet: per jaar staat in <see cref="CarnivalGroupSeason"/> hoe de wagen heette, in
/// welke categorie de groep meedeed en op welke plaats ze eindigden.
///
/// De gegevens van een inschrijving (adres, contactpersoon, aantallen) worden <b>niet</b> gekopieerd: de inschrijving
/// wijst met <c>ParadeRegistration.CarnivalGroupId</c> naar de groep, en het portal leest ze daar. Zo blijft de
/// inschrijving de enige bron en is alles van de groep zichtbaar zodra ze zich hebben ingeschreven.
/// </summary>
public sealed class CarnivalGroup : IAuditable
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// De naam waarop automatisch gekoppeld wordt: alleen letters en cijfers, in kleine letters. Een groep typt zijn
    /// naam elk jaar iets anders ("CV de Kuitenbijters", "C.V. De Kuitenbijters", "cv  de kuitenbijters"); zo komen
    /// die alle drie op dezelfde groep uit.
    /// </summary>
    public required string MatchName { get; set; }

    public string? ContactName { get; set; }

    public string? ContactPhone { get; set; }

    public string? ContactEmail { get; set; }

    /// <summary>
    /// Het eerste jaar waarin de groep meedeed, voor zover bekend; zelf in te vullen, want de geschiedenis van vóór
    /// de app staat nergens. Leeg = we weten het niet; dan rekent het portal met het oudste jaar dat hier staat.
    /// </summary>
    public int? ParticipatingSince { get; set; }

    public string? Notes { get; set; }

    /// <summary>Doet niet meer mee; blijft staan voor de geschiedenis en koppelt niet meer automatisch.</summary>
    public bool Active { get; set; } = true;

    public List<CarnivalGroupSeason> Seasons { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Alleen de letters en cijfers in kleine letters, voor het koppelen op naam. Spaties gaan er ook uit, want
    /// "C.V. de Kuitenbijters" en "CV de Kuitenbijters" zijn dezelfde groep.
    /// </summary>
    public static string Normalize(string name) => new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}

/// <summary>
/// Wat er van een groep in één carnavalsjaar bekend is. Een jaar kan hier staan zonder inschrijving (de geschiedenis
/// die je zelf invult) en een inschrijving kan er zijn zonder rij hier; het portal voegt ze samen. Een leeg veld
/// betekent "neem het over van de inschrijving", zodat een eigen antwoord nooit stilletjes overschreven wordt.
/// </summary>
public sealed class CarnivalGroupSeason
{
    public Guid GroupId { get; set; }

    /// <summary>Het jaar van carnaval, bijvoorbeeld 2027 (gelijk aan het jaar van de optochtdatum).</summary>
    public int Year { get; set; }

    /// <summary>Hoe de wagen of de loopgroep heette; leeg = het onderwerp van de inschrijving.</summary>
    public string? FloatName { get; set; }

    /// <summary>De categorie waarin ze meededen; leeg = de categorie van de inschrijving.</summary>
    public int? CategoryId { get; set; }

    /// <summary>De plaats in de uitslag van die categorie.</summary>
    public int? Place { get; set; }

    /// <summary>Gevuld toen de uitslag werd gepubliceerd; leeg = de plaats is met de hand ingevuld.</summary>
    public DateTime? PlaceFromResultsAt { get; set; }

    public string? Notes { get; set; }
}
