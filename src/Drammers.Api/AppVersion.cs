using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Drammers.Api;

/// <summary>
/// Versie van de draaiende build (<c>InformationalVersion</c>, met de commit als <c>+&lt;sha&gt;</c> via
/// <c>SourceRevisionId</c>). De deploy controleert hiermee dat na de uitrol echt de nieuwe versie draait.
/// </summary>
public static class AppVersion
{
    public const string HeaderName = "X-App-Version";

    public static readonly string Value =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "onbekend";

    public static Task WriteLiveResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.Headers[HeaderName] = Value;
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync(report.Status.ToString());
    }
}
