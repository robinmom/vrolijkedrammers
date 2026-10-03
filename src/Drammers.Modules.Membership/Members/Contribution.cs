namespace Drammers.Modules.Membership.Members;

/// <summary>
/// Soort lidmaatschap (fase 23). Een tweepersoonslidmaatschap bestaat uit twee gekoppelde leden: de betaler
/// (<see cref="TwoPersons"/>) en de partner (<see cref="Partner"/>, met <see cref="Member.PayerMemberId"/>), met één
/// contributie. Senior (65+) is geen soort: dat volgt uit de leeftijd op de incassodatum.
/// </summary>
public enum MembershipKind
{
    OnePerson,
    TwoPersons,
    Partner,
    Dansgarde,
}

/// <summary>Contributie per jaar, geldig vanaf een datum (<c>membership.ContributionRate</c>); de nieuwste die al geldt telt.</summary>
public sealed class ContributionRate
{
    public int Id { get; set; }

    public DateOnly ValidFrom { get; set; }

    public decimal OnePerson { get; set; }

    public decimal TwoPersons { get; set; }

    public decimal OnePersonSenior { get; set; }

    public decimal TwoPersonsSenior { get; set; }

    public decimal Dansgarde { get; set; }
}
