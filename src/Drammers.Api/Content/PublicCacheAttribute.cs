using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Drammers.Api.Content;

/// <summary>
/// Cache-headers voor publieke leesendpoints (fase 5-DoD): <c>Cache-Control</c> (publiek voor gasten, privé met token),
/// een ETag over de inhoud en 304 bij een ongewijzigd antwoord.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PublicCacheAttribute(int maxAgeSeconds = 60) : ResultFilterAttribute
{
    public override async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: { } value, StatusCode: null or 200 })
        {
            var http = context.HttpContext;
            var json = http.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
            var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), json));
            var etag = $"W/\"{Convert.ToHexStringLower(hash)[..32]}\"";
            var authenticated = http.Request.Headers.Authorization.Count > 0;

            http.Response.Headers.CacheControl = $"{(authenticated ? "private" : "public")}, max-age={maxAgeSeconds}";
            http.Response.Headers.ETag = etag;
            http.Response.Headers.Vary = HeaderNames.Authorization;
            if (http.Request.Headers.IfNoneMatch.Contains(etag))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
            }
        }

        await next();
    }
}
