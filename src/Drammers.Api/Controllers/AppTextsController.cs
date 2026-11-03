using Drammers.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// De teksten die in het portal zijn aangepast (Beheer → App-teksten). Alleen de aanpassingen gaan over de lijn: de
/// app heeft alle teksten zelf in de bundel en vervangt er bij het tekenen de teksten uit dit antwoord voor. Zo kan
/// het bestuur de app-teksten wijzigen zonder nieuwe app in de store.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/app-texts")]
public sealed class AppTextsController(DrammersDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AppTextsResponse>(StatusCodes.Status200OK)]
    public async Task<AppTextsResponse> Get(CancellationToken cancellationToken)
    {
        var overrides = await db.AppTextOverrides.AsNoTracking()
            .Select(o => new { o.Original, o.Value })
            .ToListAsync(cancellationToken);
        return new AppTextsResponse(overrides.ToDictionary(o => o.Original, o => o.Value));
    }
}

/// <summary>Per oorspronkelijke tekst uit de app de tekst die het toestel in de plaats moet zetten.</summary>
public sealed record AppTextsResponse(IReadOnlyDictionary<string, string> Texts);
