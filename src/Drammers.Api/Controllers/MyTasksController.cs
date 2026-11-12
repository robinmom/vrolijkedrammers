using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Tasks;
using Drammers.Modules.Content.Tasks;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// De takenlijst carnaval in de app (2026-11-12): het lid ziet alleen zijn eigen taken, zet de stand bij en voegt er
/// zelf een toe onder een bestaande categorie. Verwijderen kan hier niet; dat blijft in het portal. Een gewoon lid
/// krijgt <c>allowed: false</c> en een lege lijst, en de schrijfacties geven 403 of 404.
/// </summary>
[ApiController]
[Route("api/v1/me/tasks")]
[RequirePermission(Permissions.MemberReadOwn)]
public sealed class MyTasksController(MyTasks tasks) : ControllerBase
{
    private Guid? MemberId => CurrentUser.Get(HttpContext)!.MemberId;

    [HttpGet]
    [ProducesResponseType<MyTaskList>(StatusCodes.Status200OK)]
    public async Task<MyTaskList> Get(CancellationToken cancellationToken) =>
        MemberId is { } memberId
            ? await tasks.MineAsync(memberId, cancellationToken)
            : new MyTaskList(false, 0, "", null, 0, new MyTaskSummary(0, 0, 0, 0), [], []);

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, MyTaskRequest request, CancellationToken cancellationToken)
    {
        await tasks.UpdateMineAsync(RequireMember(), id, request.Status, request.Progress, request.Note, cancellationToken);
        return NoContent();
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Add(NewMyTaskRequest request, CancellationToken cancellationToken)
    {
        var id = await tasks.AddAsync(RequireMember(), request.ToInput(), cancellationToken);
        return Created($"/api/v1/me/tasks/{id}", new CreatedResponse(id));
    }

    private Guid RequireMember() =>
        MemberId ?? throw new DomainException(ErrorCodes.Forbidden, "Je account is niet aan een lid gekoppeld.", DomainErrorKind.Forbidden);
}

public sealed record MyTaskRequest(CarnivalTaskStatus Status, [param: Range(0, 100)] int Progress, [param: StringLength(500)] string? Note);

public sealed record NewMyTaskRequest(
    Guid CategoryId,
    [param: Required, StringLength(200, MinimumLength = 1)] string Title,
    [param: StringLength(2000)] string? Description,
    DateOnly? DueOn,
    CarnivalTaskStatus Status = CarnivalTaskStatus.NotStarted,
    [param: Range(0, 100)] int Progress = 0)
{
    public NewTaskInput ToInput() => new(CategoryId, Title, Description, DueOn, Status, Progress);
}
