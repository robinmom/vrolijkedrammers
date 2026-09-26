using Drammers.Infrastructure.Identity;
using Drammers.Modules.Identity.Devices;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Authorization;

/// <summary>
/// Apparaatcheck (fase 9): stuurt de app een installatie-id mee (<c>X-Device-Id</c>) en is dat apparaat afgemeld, dan
/// volgt 401 <c>DEVICE_REVOKED</c> en wist de app de tokens. Zonder header (portal) verandert er niets.
/// </summary>
public static class DeviceCheck
{
    public const string HeaderName = "X-Device-Id";

    public static IApplicationBuilder UseDeviceCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var installationId = context.Request.Headers[HeaderName].ToString();
            if (!string.IsNullOrEmpty(installationId) && context.User.Identity?.IsAuthenticated == true
                && await CurrentUser.ResolveAsync(context) is { } user)
            {
                var status = await context.RequestServices.GetRequiredService<MyAccount>()
                    .TouchDeviceAsync(user.UserId, installationId, context.RequestAborted);
                if (status == DeviceStatus.Revoked)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status401Unauthorized,
                            Detail = "Dit apparaat is afgemeld. Log opnieuw in.",
                            Extensions = { ["code"] = ErrorCodes.DeviceRevoked },
                        },
                    });
                    return;
                }
            }

            await next(context);
        });
}
