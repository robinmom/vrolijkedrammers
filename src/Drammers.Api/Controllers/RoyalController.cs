using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Prins(es) en adjudanten in de app (2026-10-10): begroeting en eigen informatie op de eerste pagina.</summary>
[ApiController]
[Route("api/v1/me/royal")]
[RequirePermission(Permissions.MemberReadOwn)]
public sealed class MyRoyalController(RoyalHousehold royal) : ControllerBase
{
    /// <summary>De begroeting en informatie; <c>204</c> zonder geldige rol prins(es) of adjudant.</summary>
    [HttpGet]
    [ProducesResponseType<RoyalGreeting>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        await royal.ForUserAsync(CurrentUser.Get(HttpContext)!.UserId, cancellationToken) is { } greeting ? Ok(greeting) : NoContent();
}

/// <summary>Portal → Prins: wie dit jaar prins(es) en adjudant is, en hun informatie in de app.</summary>
[ApiController]
[Route("api/v1/admin/royal")]
[RequirePermission(Permissions.RoleManage)]
public sealed class AdminRoyalController(RoyalHousehold royal) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<RoyalOverview>(StatusCodes.Status200OK)]
    public Task<RoyalOverview> Get(CancellationToken cancellationToken) => royal.OverviewAsync(cancellationToken);

    [HttpPut("info/{roleCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetInfo(string roleCode, RoleInfoRequest request, CancellationToken cancellationToken)
    {
        await royal.SetInfoAsync(roleCode, request.Body, cancellationToken);
        return NoContent();
    }
}

public sealed record RoleInfoRequest([StringLength(20000)] string? Body);
