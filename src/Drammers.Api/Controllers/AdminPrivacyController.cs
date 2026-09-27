using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Privacy;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>AVG-verzoeken (fase 9b): overzicht, export en wissen namens een lid; recht <c>member.privacy</c>, geaudit.</summary>
[ApiController]
[Route("api/v1/admin")]
[RequirePermission(Permissions.MemberPrivacy)]
public sealed class AdminPrivacyController(DrammersDbContext db, MyAccount account) : ControllerBase
{
    [HttpGet("privacy-requests")]
    [ProducesResponseType<PagedResult<PrivacyRequestResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<PrivacyRequestResponse>> Search([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<PrivacyRequestResponse>.Normalize(page, pageSize);
        var total = await db.PrivacyRequests.CountAsync(cancellationToken);
        var rows = await db.PrivacyRequests.AsNoTracking().OrderByDescending(r => r.RequestedAt).Skip((p - 1) * size).Take(size)
            .GroupJoin(db.Users, r => r.RequestedBy, u => u.Id, (r, us) => new { r, us })
            .SelectMany(x => x.us.DefaultIfEmpty(), (x, by) => new PrivacyRequestResponse(
                x.r.Id, x.r.Type, x.r.Status, x.r.UserId, x.r.SubjectName, x.r.RequestedBy == x.r.UserId ? null : by!.DisplayName,
                x.r.RequestedAt, x.r.CompletedAt, x.r.FilePath != null ? x.r.ExpiresAt : null))
            .ToListAsync(cancellationToken);
        return new PagedResult<PrivacyRequestResponse>(rows, p, size, total);
    }

    /// <summary>Export namens een lid (bijv. een verzoek per brief of e-mail); de link is 15 minuten geldig.</summary>
    [HttpPost("users/{id:guid}/privacy-export")]
    [ProducesResponseType<PrivacyExportResponse>(StatusCodes.Status200OK)]
    public async Task<PrivacyExportResponse> Export(Guid id, CancellationToken cancellationToken) =>
        PrivacyExportResponse.From(await account.CreateExportAsync(id, cancellationToken));

    [HttpGet("privacy-requests/{id:guid}/download")]
    [ProducesResponseType<PrivacyExportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PrivacyExportResponse> Download(Guid id, CancellationToken cancellationToken) =>
        PrivacyExportResponse.From(await account.GetExportAsync(null, id, cancellationToken));

    /// <summary>Alle app-gegevens van een gebruiker wissen (AVG art. 17); de ledenadministratie in e-Boekhouden blijft.</summary>
    [HttpPost("users/{id:guid}/erase")]
    [ProducesResponseType<ErasureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ErasureResponse> Erase(Guid id, EraseRequest request, CancellationToken cancellationToken)
    {
        if (request.Confirmation != EraseRequest.Expected)
        {
            throw new DomainException(ErrorCodes.Validation, $"Typ '{EraseRequest.Expected}' om de gegevens te wissen.");
        }

        var result = await account.EraseAsync(id, cancellationToken);
        return new ErasureResponse(result.Applications, result.AccountRequests, result.Logins, result.GuardianRelations, result.Devices);
    }
}

public sealed record PrivacyRequestResponse(
    Guid Id, PrivacyRequestType Type, PrivacyRequestStatus Status, Guid UserId, string? SubjectName, string? RequestedByBoard,
    DateTime RequestedAt, DateTime? CompletedAt, DateTime? DownloadableUntil);

public sealed record EraseRequest([param: Required] string Confirmation)
{
    public const string Expected = "WISSEN";
}

public sealed record ErasureResponse(int Applications, int AccountRequests, int Logins, int GuardianRelations, int Devices);
