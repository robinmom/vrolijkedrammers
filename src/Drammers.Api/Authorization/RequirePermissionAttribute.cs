using Microsoft.AspNetCore.Authorization;

namespace Drammers.Api.Authorization;

/// <summary>Endpoint vereist een permission (docs/07 §5); impliceert een actief, bekend account.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission)
    : AuthorizeAttribute(PermissionPolicyProvider.PolicyPrefix + permission)
{
    public string Permission { get; } = permission;
}

/// <summary>Endpoint voor elke ingelogde gebruiker met een actief, bekend account (bijv. <c>GET /me</c>).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireActiveUserAttribute() : AuthorizeAttribute(PermissionPolicyProvider.ActiveUserPolicy);
