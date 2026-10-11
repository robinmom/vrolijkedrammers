using Drammers.Modules.Membership.Applications;
using Drammers.Modules.Membership.Members;

namespace Drammers.Infrastructure.Members;

/// <summary>
/// Wanneer een lid bij een gedeeld e-mailadres als kind telt (2026-10-11), overal hetzelfde: bij splitsen, bij een
/// app-account aanmaken en bij het samenvoegen van accounts. Kind = jonger dan 15, of zonder geboortedatum maar lid van de
/// dansgarde (soort, groep of status uit e-Boekhouden). Het account met dat adres hoort dan bij de volwassene.
/// </summary>
public static class ChildAccounts
{
    public static bool IsChild(Member m, DateOnly today) =>
        Guardians.AgeOn(m.BirthDate, today) is { } age
            ? age < MembershipApplication.MinimumAgeOwnAccount
            : m.MembershipKind == MembershipKind.Dansgarde || Dansgarde.IsDansgarde(m.ParadeGroupName)
              || (m.EbStatusRaw?.Contains("dansgarde", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Dezelfde regel als filter in de database.</summary>
    public static IQueryable<Member> WhereNotChild(this IQueryable<Member> members, DateOnly today)
    {
        var cutoff = today.AddYears(-MembershipApplication.MinimumAgeOwnAccount);
        return members.Where(m => m.BirthDate != null
            ? m.BirthDate <= cutoff
            : !(m.MembershipKind == MembershipKind.Dansgarde || m.ParadeGroupName == Dansgarde.GroupValue
                || (m.EbStatusRaw != null && m.EbStatusRaw.Contains("dansgarde"))));
    }
}
