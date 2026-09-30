using Drammers.Api.Authorization;
using Drammers.Infrastructure.Sales;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Drammers.Api.Controllers;

/// <summary>
/// Kassa (fase 19c): munten-QR scannen en "Bestelling uitgegeven" met <c>sale.collect</c> (rol Kassa). Alleen online;
/// elke scan en uitgifte komt in de Kassalog.
/// </summary>
[ApiController]
[Route("api/v1/kassa")]
[RequirePermission(Permissions.SaleCollect)]
public sealed class KassaController(TokenCollection kassa) : ControllerBase
{
    private Guid UserId => CurrentUser.Get(HttpContext)!.UserId;

    [HttpPost("scan")]
    [EnableRateLimiting(AuthorizationSetup.ScannerPolicy)]
    [ProducesResponseType<KassaResult>(StatusCodes.Status200OK)]
    public Task<KassaResult> Scan(ScanRequest request, CancellationToken cancellationToken) =>
        kassa.ScanAsync(UserId, Request.Headers[DeviceCheck.HeaderName].ToString(), request.Code, cancellationToken);

    [HttpPost("scans/{id:guid}/issue")]
    [ProducesResponseType<KassaResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<KassaResult> Issue(Guid id, CancellationToken cancellationToken) => kassa.IssueAsync(UserId, id, cancellationToken);
}
