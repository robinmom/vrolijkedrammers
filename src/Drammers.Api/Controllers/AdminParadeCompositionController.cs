using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Optocht samenstellen (fase 12b): de volgorde bekijken (<c>parade.read</c>), slepen/opslaan (<c>parade.manage</c>) en
/// startnummers genereren uit de volgorde met eerst een preview (<c>parade.assign-start-number</c>).
/// </summary>
[ApiController]
[Route("api/v1/admin/parade-composition")]
[RequirePermission(Permissions.ParadeRead)]
public sealed class AdminParadeCompositionController(ParadeComposition composition) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Composition>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<Composition> Get(CancellationToken cancellationToken) => composition.GetAsync(cancellationToken);

    /// <summary>Nieuwe volgorde; 412 als iemand anders intussen de volgorde heeft gewijzigd.</summary>
    [HttpPut("order")]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType<CompositionVersionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<CompositionVersionResponse> SaveOrder(SaveOrderRequest request, CancellationToken cancellationToken) =>
        new(await composition.SaveOrderAsync(request.Version, request.OrderedIds, cancellationToken));

    [HttpPost("start-numbers/preview")]
    [RequirePermission(Permissions.ParadeAssignStartNumber)]
    [ProducesResponseType<StartNumberPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<StartNumberPreview> Preview(GenerateStartNumbersRequest request, CancellationToken cancellationToken) =>
        composition.PreviewAsync(request.Mode, request.StartAt, cancellationToken);

    [HttpPost("start-numbers/apply")]
    [RequirePermission(Permissions.ParadeAssignStartNumber)]
    [ProducesResponseType<GenerateStartNumbersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<GenerateStartNumbersResponse> Apply(ApplyStartNumbersRequest request, CancellationToken cancellationToken) =>
        new(await composition.ApplyAsync(request.Version, request.Mode, request.StartAt, request.Confirmation, cancellationToken));
}

public sealed record SaveOrderRequest(int Version, [Required, MaxLength(500)] IReadOnlyList<Guid> OrderedIds);

public sealed record CompositionVersionResponse(int Version);

public sealed record GenerateStartNumbersRequest(StartNumberMode Mode, [Range(1, 9999)] int StartAt = 1);

public sealed record ApplyStartNumbersRequest(int Version, StartNumberMode Mode, [Range(1, 9999)] int StartAt = 1, [StringLength(20)] string? Confirmation = null);

public sealed record GenerateStartNumbersResponse(int Changed);
