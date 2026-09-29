namespace Drammers.Infrastructure.Members;

/// <summary>
/// Hoeveel personen een lidmaatschap telt (fase 19, groepskaarten pronkzitting). Het soort lid komt uit e-Boekhouden
/// (het als categorie gemapte veld): "Tweepersoonslid DVD" telt 2; "Eénpersoonslid DVD", "Lidmaatschap dansgarde DVD"
/// en alles wat daar niet aan voldoet telt 1.
/// </summary>
public static class MembershipWeights
{
    public const string TwoPersons = "Tweepersoonslid DVD";

    public static int Persons(string? category) =>
        string.Equals(category?.Trim(), TwoPersons, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
}
