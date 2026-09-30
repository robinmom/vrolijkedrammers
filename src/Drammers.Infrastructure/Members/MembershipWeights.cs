namespace Drammers.Infrastructure.Members;

/// <summary>
/// Hoeveel personen een lidmaatschap telt (fase 19, groepskaarten pronkzitting). Het soort lid staat in e-Boekhouden in
/// het veld dat als <b>status</b> is gemapt (bij De Vrolijke Drammers vrij veld 1): "Tweepersoonslid DVD" telt 2;
/// "Eénpersoonslid DVD", "Lidmaatschap dansgarde DVD" en alles wat daar niet aan voldoet telt 1.
/// </summary>
public static class MembershipWeights
{
    public const string TwoPersons = "Tweepersoonslid DVD";

    public static int Persons(string? status) =>
        string.Equals(status?.Trim(), TwoPersons, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
}
