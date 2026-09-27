namespace Drammers.Modules.Membership.Members;

/// <summary>
/// Een lid dat volledig uit de app is verwijderd, maar (nog) in e-Boekhouden staat: de ledensync slaat dit lidnummer
/// over, zodat het lid niet terugkomt. Bewust alleen het lidnummer (dataminimalisatie). Opheffen = het lid komt bij de
/// volgende sync terug.
/// </summary>
public sealed class ExcludedMember
{
    public required string MemberNumber { get; set; }

    public DateTime ExcludedAt { get; set; }

    public Guid? ExcludedBy { get; set; }
}
