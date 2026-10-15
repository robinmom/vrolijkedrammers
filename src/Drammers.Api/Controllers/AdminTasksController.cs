using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Tasks;
using Drammers.Modules.Content.Tasks;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Takenlijst carnaval (2026-10-15): taken, categorieën, kopiëren naar volgend jaar en rapportage.</summary>
[ApiController]
[Route("api/v1/admin/tasks")]
[RequirePermission(Permissions.TaskManage)]
public sealed class AdminTasksController(CarnivalTasks tasks) : ControllerBase
{
    /// <summary>De takenlijst van een carnavalsjaar (zonder jaar: het actieve), met de totalen.</summary>
    [HttpGet]
    [ProducesResponseType<TaskYearView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TaskYearView> Year([FromQuery] int? yearId, CancellationToken cancellationToken) => tasks.YearAsync(yearId, cancellationToken);

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create([FromQuery, Required] int yearId, TaskRequest request, CancellationToken cancellationToken) =>
        Created((string?)null, new CreatedResponse(await tasks.CreateAsync(yearId, request.ToInput(), cancellationToken)));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, TaskRequest request, CancellationToken cancellationToken)
    {
        await tasks.UpdateAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await tasks.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>De aangevinkte taken naar het volgende carnavalsjaar kopiëren.</summary>
    [HttpPost("copy")]
    [ProducesResponseType<TasksCopiedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<TasksCopiedResponse> Copy(TaskCopyRequest request, CancellationToken cancellationToken) =>
        new(await tasks.CopyToNextYearAsync(request.FromYearId, request.TaskIds, cancellationToken));

    [HttpGet("report")]
    [ProducesResponseType<TaskReport>(StatusCodes.Status200OK)]
    public Task<TaskReport> Report([FromQuery] int? yearId, CancellationToken cancellationToken) => tasks.ReportAsync(yearId, cancellationToken);

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<TaskCategoryView>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<TaskCategoryView>> Categories(CancellationToken cancellationToken) => tasks.CategoriesAsync(cancellationToken);

    [HttpPost("categories")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> CreateCategory(TaskCategoryRequest request, CancellationToken cancellationToken) =>
        Created((string?)null, new CreatedResponse(await tasks.SaveCategoryAsync(null, request.Name, request.SortOrder, request.Active, cancellationToken)));

    [HttpPut("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateCategory(Guid id, TaskCategoryRequest request, CancellationToken cancellationToken)
    {
        await tasks.SaveCategoryAsync(id, request.Name, request.SortOrder, request.Active, cancellationToken);
        return NoContent();
    }

    [HttpDelete("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken)
    {
        await tasks.DeleteCategoryAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Actieve leden zoeken om aan een taak te koppelen.</summary>
    [HttpGet("members")]
    [ProducesResponseType<IReadOnlyList<TaskPerson>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<TaskPerson>> Members([FromQuery, Required, StringLength(100)] string search, CancellationToken cancellationToken) =>
        tasks.SearchMembersAsync(search, cancellationToken);
}

public sealed record TaskRequest(
    Guid CategoryId,
    [param: Required, StringLength(200)] string Title,
    [param: StringLength(2000)] string? Description,
    CarnivalTaskStatus Status,
    [param: Range(0, 100)] int Progress,
    DateOnly? DueOn,
    [param: MaxLength(30)] IReadOnlyList<Guid>? MemberIds)
{
    public TaskInput ToInput() => new(CategoryId, Title, Description, Status, Progress, DueOn, MemberIds ?? []);
}

public sealed record TaskCopyRequest(int FromYearId, [param: Required] IReadOnlyList<Guid> TaskIds);

public sealed record TasksCopiedResponse(int Copied);

public sealed record TaskCategoryRequest([param: Required, StringLength(80)] string Name, int SortOrder, bool Active = true);
