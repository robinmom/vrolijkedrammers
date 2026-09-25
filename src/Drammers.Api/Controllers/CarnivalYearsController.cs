using Drammers.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Het actieve carnavalsjaar, o.a. voor de countdown in de app (docs/05 §2).</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/carnival-years")]
public sealed class CarnivalYearsController(DrammersDbContext db) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType<CarnivalYearResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CarnivalYearResponse>> GetCurrent(CancellationToken cancellationToken)
    {
        var year = await db.CarnivalYears.AsNoTracking()
            .Where(y => y.Active)
            .Select(y => new CarnivalYearResponse(y.Id, y.Name, y.StartDate, y.EndDate, y.CarnivalStartDate, y.CarnivalEndDate))
            .SingleOrDefaultAsync(cancellationToken);

        return year is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Er is geen actief carnavalsjaar.")
            : year;
    }
}

public sealed record CarnivalYearResponse(
    int Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly CarnivalStartDate,
    DateOnly CarnivalEndDate);
