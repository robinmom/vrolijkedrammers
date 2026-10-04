using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Advertisers;
using Drammers.Modules.Membership.Advertisers;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// De collectant in de app (fase 27b-2): de eigen adverteerders van de lopende campagne afvinken (opgehaald of stopt)
/// en een nieuwe adverteerder aanmelden. Alleen voor kaderleden; anderen krijgen <c>isCollector: false</c>.
/// </summary>
[ApiController]
[Route("api/v1/me/advertisers")]
[RequirePermission(Permissions.MemberReadOwn)]
public sealed class MyAdvertisersController(AdvertiserAdministration advertisers) : ControllerBase
{
    private Guid? MemberId => CurrentUser.Get(HttpContext)!.MemberId;

    [HttpGet]
    [ProducesResponseType<MyAdvertisers>(StatusCodes.Status200OK)]
    public async Task<MyAdvertisers> Get(CancellationToken cancellationToken) =>
        MemberId is { } memberId
            ? await advertisers.MineAsync(memberId, cancellationToken)
            : new MyAdvertisers(false, await advertisers.CampaignYearAsync(cancellationToken), []);

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetStatus(Guid id, AdvertiserStatusRequest request, CancellationToken cancellationToken)
    {
        await advertisers.SetMyStatusAsync(RequireMember(), id, request.Status, request.Amount, request.Note, cancellationToken);
        return NoContent();
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Add(NewAdvertiserRequest request, CancellationToken cancellationToken)
    {
        var id = await advertisers.AddFromAppAsync(RequireMember(), request.ToInput(), cancellationToken);
        return Created($"/api/v1/me/advertisers/{id}", new CreatedResponse(id));
    }

    private Guid RequireMember() =>
        MemberId ?? throw new DomainException(ErrorCodes.Forbidden, "Je account is niet aan een lid gekoppeld.", DomainErrorKind.Forbidden);
}

public sealed record NewAdvertiserRequest(
    [Required, StringLength(200, MinimumLength = 1)] string CompanyName,
    [StringLength(150)] string? ContactName,
    [StringLength(30)] string? Phone,
    [StringLength(254)] string? Email,
    [StringLength(200)] string? AddressLine,
    [StringLength(10)] string? PostalCode,
    [StringLength(100)] string? City,
    AdvertiserKind Kind,
    AdvertiserPayment Payment,
    [Range(0, 100000)] decimal Amount,
    [StringLength(40)] string? Iban,
    bool MandateConsent,
    [StringLength(500)] string? Note)
{
    public NewAdvertiserInput ToInput() =>
        new(CompanyName, ContactName, Phone, Email, AddressLine, PostalCode, City, Kind, Payment, Amount, Iban, MandateConsent, Note);
}
