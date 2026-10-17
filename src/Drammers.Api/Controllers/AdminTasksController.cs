using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Tasks;
using Drammers.Modules.Content.Tasks;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Takenlijst carnaval (2026-10-15): taken, categorieën, kopiëren naar volgend jaar en rapportage.</summary>
[Route("api/v1/admin/tasks")]
[RequirePermission(Permissions.TaskManage)]
public sealed class AdminTasksController(CarnivalTasks tasks) : TaskListControllerBase(tasks)
{
    protected override TaskList List => TaskList.Carnival;
}

/// <summary>Takenlijst van het Dansgardefestival (2026-10-15): dezelfde functies, voor de organisatie van het festival.</summary>
[Route("api/v1/admin/festival/tasks")]
[RequirePermission(Permissions.FestivalManage)]
public sealed class AdminFestivalTasksController(CarnivalTasks tasks) : TaskListControllerBase(tasks)
{
    protected override TaskList List => TaskList.Festival;

    /// <summary>
    /// Het draaiboek inlezen (Excel, tabblad Actiepunten: Actie, Wie, Plan, opmerkingen). Kopjes worden categorieën; taken
    /// die er al zijn worden overgeslagen.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(5_000_000)]
    [ProducesResponseType<TaskImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<TaskImportResult> Import([FromQuery] int? yearId, IFormFile file, CancellationToken cancellationToken)
    {
        List<TaskImportRow> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("Actiepunten", StringComparison.OrdinalIgnoreCase))
                ?? workbook.Worksheets.First();
            var header = sheet.RowsUsed().FirstOrDefault(r => r.Cell(1).GetString().Trim().Equals("Actie", StringComparison.OrdinalIgnoreCase))
                ?? throw new DomainException(ErrorCodes.Validation, "Geen kopregel met \"Actie\" gevonden in het tabblad Actiepunten.");
            static DateOnly? Date(IXLCell cell) => !cell.IsEmpty() && cell.TryGetValue<DateTime>(out var d) ? DateOnly.FromDateTime(d) : null;
            rows = [.. sheet.RowsUsed().Where(r => r.RowNumber() > header.RowNumber())
                .Select(r => new TaskImportRow(r.Cell(1).GetString(), r.Cell(2).GetString(), Date(r.Cell(3)), r.Cell(4).GetString()))];
        }
        catch (Exception ex) when (ex is not DomainException and not OperationCanceledException)
        {
            throw new DomainException(ErrorCodes.Validation, "Dit is geen leesbaar Excel-bestand (.xlsx).");
        }

        return await Tasks.ImportAsync(List, yearId, rows, cancellationToken);
    }
}

/// <summary>De endpoints van een takenlijst; de lijst en het recht komen van de afgeleide controller.</summary>
[ApiController]
public abstract class TaskListControllerBase(CarnivalTasks tasks) : ControllerBase
{
    protected abstract TaskList List { get; }

    protected CarnivalTasks Tasks => tasks;

    /// <summary>De takenlijst van een carnavalsjaar (zonder jaar: het actieve), met de totalen.</summary>
    [HttpGet]
    [ProducesResponseType<TaskYearView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TaskYearView> Year([FromQuery] int? yearId, CancellationToken cancellationToken) => tasks.YearAsync(List, yearId, cancellationToken);

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create([FromQuery, Required] int yearId, TaskRequest request, CancellationToken cancellationToken) =>
        Created((string?)null, new CreatedResponse(await tasks.CreateAsync(List, yearId, request.ToInput(), cancellationToken)));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, TaskRequest request, CancellationToken cancellationToken)
    {
        await tasks.UpdateAsync(List, id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await tasks.DeleteAsync(List, id, cancellationToken);
        return NoContent();
    }

    /// <summary>De aangevinkte taken naar het volgende carnavalsjaar kopiëren.</summary>
    [HttpPost("copy")]
    [ProducesResponseType<TasksCopiedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<TasksCopiedResponse> Copy(TaskCopyRequest request, CancellationToken cancellationToken) =>
        new(await tasks.CopyToNextYearAsync(List, request.FromYearId, request.TaskIds, cancellationToken));

    [HttpGet("report")]
    [ProducesResponseType<TaskReport>(StatusCodes.Status200OK)]
    public Task<TaskReport> Report([FromQuery] int? yearId, CancellationToken cancellationToken) => tasks.ReportAsync(List, yearId, cancellationToken);

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<TaskCategoryView>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<TaskCategoryView>> Categories(CancellationToken cancellationToken) => tasks.CategoriesAsync(List, cancellationToken);

    [HttpPost("categories")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> CreateCategory(TaskCategoryRequest request, CancellationToken cancellationToken) =>
        Created((string?)null, new CreatedResponse(await tasks.SaveCategoryAsync(List, null, request.Name, request.SortOrder, request.Active, cancellationToken)));

    [HttpPut("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateCategory(Guid id, TaskCategoryRequest request, CancellationToken cancellationToken)
    {
        await tasks.SaveCategoryAsync(List, id, request.Name, request.SortOrder, request.Active, cancellationToken);
        return NoContent();
    }

    [HttpDelete("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken)
    {
        await tasks.DeleteCategoryAsync(List, id, cancellationToken);
        return NoContent();
    }

    /// <summary>Actieve leden zoeken om aan een taak te koppelen (alleen naam; bij het festival minimaal 3 tekens, geen lidnummer).</summary>
    [HttpGet("members")]
    [ProducesResponseType<IReadOnlyList<TaskPerson>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<TaskPerson>> Members([FromQuery, Required, StringLength(100)] string search, CancellationToken cancellationToken) =>
        tasks.SearchMembersAsync(List, search, cancellationToken);
}

public sealed record TaskRequest(
    Guid CategoryId,
    [param: Required, StringLength(200)] string Title,
    [param: StringLength(2000)] string? Description,
    CarnivalTaskStatus Status,
    [param: Range(0, 100)] int Progress,
    DateOnly? DueOn,
    [param: MaxLength(30)] IReadOnlyList<Guid>? MemberIds,
    [param: StringLength(300)] string? OtherPeople = null)
{
    public TaskInput ToInput() => new(CategoryId, Title, Description, Status, Progress, DueOn, MemberIds ?? [], OtherPeople);
}

public sealed record TaskCopyRequest(int FromYearId, [param: Required] IReadOnlyList<Guid> TaskIds);

public sealed record TasksCopiedResponse(int Copied);

public sealed record TaskCategoryRequest([param: Required, StringLength(80)] string Name, int SortOrder, bool Active = true);
