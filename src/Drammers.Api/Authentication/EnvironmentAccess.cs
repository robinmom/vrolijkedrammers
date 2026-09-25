using System.Security.Claims;

namespace Drammers.Api.Authentication;

/// <summary>
/// Tweede slot op Dev/Acc naast "Require user assignment" (B-02): het token moet de omgeving
/// in de claim <c>environmentAccess</c> bevatten (kommagescheiden, bijv. <c>dev,acc</c>).
/// </summary>
public static class EnvironmentAccess
{
    public static bool IsAllowed(ClaimsPrincipal principal, string claimType, string? requiredEnvironment)
    {
        if (string.IsNullOrWhiteSpace(requiredEnvironment))
        {
            return true;
        }

        return principal.FindAll(claimType)
            .SelectMany(claim => claim.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(value => string.Equals(value, requiredEnvironment, StringComparison.OrdinalIgnoreCase));
    }
}
