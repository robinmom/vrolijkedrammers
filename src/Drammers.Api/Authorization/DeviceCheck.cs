using Drammers.Infrastructure.Identity;
using Drammers.Modules.Identity.Devices;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Drammers.Api.Authorization;

/// <summary>
/// Apparaatcheck (fase 9): stuurt de app een installatie-id mee (<c>X-Device-Id</c>) en is dat apparaat afgemeld, dan
/// volgt 401 <c>DEVICE_REVOKED</c> en wist de app de tokens. Zonder header (portal) verandert er niets, behalve op
/// endpoints met <see cref="RequireDeviceAttribute"/>: die weigeren een aanroep zonder header.
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

/// <summary>
/// Endpoint dat aan een toestel gebonden is (QR, ticketkoppeling, scanner): zonder <c>X-Device-Id</c> volgt 400
/// <c>DEVICE_REQUIRED</c>. Zo kan een afgemeld toestel de apparaatcheck niet omzeilen door de header weg te laten.
/// Als filter (na de autorisatie): zonder recht blijft het antwoord 403.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireDeviceAttribute : ActionFilterAttribute
{
    public override Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!string.IsNullOrWhiteSpace(context.HttpContext.Request.Headers[DeviceCheck.HeaderName].ToString()))
        {
            return next();
        }

        var problem = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>().CreateProblemDetails(
            context.HttpContext, StatusCodes.Status400BadRequest, detail: "Dit kan alleen in de app op een aangemeld toestel. Log opnieuw in.");
        problem.Extensions["code"] = ErrorCodes.DeviceRequired;
        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
        return Task.CompletedTask;
    }
}
