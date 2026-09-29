using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Ticketing;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Toegangsstatistieken voor het bestuur (fase 14/15, <c>ticket.read</c>): per dag/activiteit, overzicht en dashboardblok.</summary>
[ApiController]
[Route("api/v1/admin/access-stats")]
[RequirePermission(Permissions.TicketRead)]
public sealed class AdminAccessStatsController(AccessStatistics statistics) : ControllerBase
{
    /// <summary>Statistieken van één carnavalsdag (<c>dag-2027-02-13</c>) of activiteit (event-id).</summary>
    [HttpGet]
    [ProducesResponseType<AccessStats>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AccessStats> Get([FromQuery, Required, StringLength(40)] string key, CancellationToken cancellationToken) =>
        await statistics.StatsAsync(await statistics.MomentAsync(key, cancellationToken), cancellationToken);

    /// <summary>Alle carnavalsdagen en activiteiten met toegangscontrole naast elkaar.</summary>
    [HttpGet("overview")]
    [ProducesResponseType<IReadOnlyList<AccessStats>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AccessStats>> Overview(CancellationToken cancellationToken) => statistics.OverviewAsync(cancellationToken);

    /// <summary>Blok op het dashboard: lopende (of laatste) dag/activiteit en "klaar voor de deur".</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<AccessDashboard>(StatusCodes.Status200OK)]
    public Task<AccessDashboard> Dashboard(CancellationToken cancellationToken) => statistics.DashboardAsync(cancellationToken);
}
