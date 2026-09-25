namespace Drammers.Modules.Content.CarnivalYears;

/// <summary>Carnavalsjaar: de kern-dimensie waar events, optocht en tickets aan hangen (docs/04 §5, §13).</summary>
public sealed class CarnivalYear
{
    public int Id { get; set; }

    /// <summary>Bijvoorbeeld <c>2026/2027</c>.</summary>
    public required string Name { get; set; }

    /// <summary>Seizoen, bijvoorbeeld 11-11 t/m Aswoensdag.</summary>
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>De carnavalsdagen zelf (zaterdag t/m dinsdag).</summary>
    public DateOnly CarnivalStartDate { get; set; }

    public DateOnly CarnivalEndDate { get; set; }

    /// <summary>Precies één carnavalsjaar is actief (filtered unique index).</summary>
    public bool Active { get; set; }
}
