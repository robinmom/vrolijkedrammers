using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Carnavalsjaren beheren; er is altijd precies één actief jaar (docs/04 §5).</summary>
[ApiController]
[Route("api/v1/admin/carnival-years")]
[RequirePermission(Permissions.ConfigManage)]
public sealed class AdminCarnivalYearsController(DrammersDbContext db, ConfigurationAdministration administration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminCarnivalYearResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AdminCarnivalYearResponse>> GetAll(CancellationToken cancellationToken) =>
        await db.CarnivalYears.AsNoTracking().OrderByDescending(y => y.StartDate)
            .Select(y => new AdminCarnivalYearResponse(y.Id, y.Name, y.StartDate, y.EndDate, y.CarnivalStartDate, y.CarnivalEndDate, y.Active))
            .ToListAsync(cancellationToken);

    [HttpPost]
    [ProducesResponseType<AdminCarnivalYearResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AdminCarnivalYearResponse>> Create(CarnivalYearRequest request, CancellationToken cancellationToken)
    {
        var y = await administration.CreateCarnivalYearAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/carnival-years/{y.Id}",
            new AdminCarnivalYearResponse(y.Id, y.Name, y.StartDate, y.EndDate, y.CarnivalStartDate, y.CarnivalEndDate, y.Active));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(int id, CarnivalYearRequest request, CancellationToken cancellationToken)
    {
        await administration.UpdateCarnivalYearAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:int}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        await administration.ActivateCarnivalYearAsync(id, cancellationToken);
        return NoContent();
    }
}

public sealed record AdminCarnivalYearResponse(
    int Id, string Name, DateOnly StartDate, DateOnly EndDate, DateOnly CarnivalStartDate, DateOnly CarnivalEndDate, bool Active);

public sealed record CarnivalYearRequest(
    [Required, RegularExpression(@"^\d{4}/\d{4}$")] string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly CarnivalStartDate,
    DateOnly CarnivalEndDate)
{
    public CarnivalYearInput ToInput() => new(Name, StartDate, EndDate, CarnivalStartDate, CarnivalEndDate);
}
