using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Drammers.Api.Authorization;

/// <summary>
/// Dynamische policies <c>perm:&lt;code&gt;</c> (docs/07 §5). De standaard- en fallbackpolicy vereisen een actief account,
/// zodat een endpoint zonder annotatie nooit open staat (deny by default).
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string PolicyPrefix = "perm:";
    public const string ActiveUserPolicy = "active-user";

    public static readonly AuthorizationPolicy ActiveUser = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new ActiveUserRequirement())
        .Build();

    /// <summary>
    /// Voor endpoints zonder annotatie: een actief account. Zonder <c>RequireAuthenticatedUser</c>, zodat een onbekende
    /// route (geen endpoint) gewoon 404 geeft; de handler laat alleen dat geval door.
    /// </summary>
    public static readonly AuthorizationPolicy Fallback = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .AddRequirements(new ActiveUserRequirement())
        .Build();

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName == ActiveUserPolicy)
        {
            return ActiveUser;
        }

        if (policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new ActiveUserRequirement(), new PermissionRequirement(policyName[PolicyPrefix.Length..]))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

public sealed class ActiveUserRequirement : IAuthorizationRequirement;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
