using Drammers.Api.Authorization;
using Drammers.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>De ingelogde gebruiker: profiel, rollen, permissions en features (docs/05 §3).</summary>
[ApiController]
[Route("api/v1/me")]
[RequireActiveUser]
public sealed class MeController(AppConfigReader appConfig) : ControllerBase
{
    /// <summary>Permissions zijn alleen bedoeld voor het tonen of verbergen van UI; de server blijft leidend (docs/07 §1).</summary>
    [HttpGet]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<MeResponse> Get(CancellationToken cancellationToken)
    {
        var user = CurrentUser.Get(HttpContext)!;
        var config = await appConfig.GetAsync(cancellationToken);
        return new MeResponse(
            user.UserId,
            user.Email,
            user.DisplayName,
            user.MemberId,
            [.. user.Roles.Select(r => new MeRole(r.Code, r.Name))],
            [.. user.Permissions.Order(StringComparer.Ordinal)],
            config.Features);
    }
}

public sealed record MeResponse(
    Guid Id,
    string Email,
    string DisplayName,
    Guid? MemberId,
    IReadOnlyList<MeRole> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyDictionary<string, bool> Features);

public sealed record MeRole(string Code, string Name);
