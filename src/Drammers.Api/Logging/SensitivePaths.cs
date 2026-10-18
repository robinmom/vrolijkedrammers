using Serilog.Core;
using Serilog.Events;

namespace Drammers.Api.Logging;

/// <summary>
/// Routes met een geheim token in het pad (persoonlijke link uit een mail, voorbeeld van een mailing). Wie de logs of
/// Application Insights kan lezen, mag die links niet kunnen nabootsen: in het gelogde pad wordt het token vervangen
/// door het route-template (<c>{token}</c>). Tokens in de query string redigeert OpenTelemetry al standaard; Serilog
/// logt de query string niet.
/// </summary>
public static class SensitivePaths
{
    /// <summary>Prefixen waarachter één padsegment het token is.</summary>
    private static readonly string[] TokenPrefixes =
    [
        "/api/v1/membership-applications/split/",
        $"{Content.MailingPreviewDocuments.BasePath}/",
    ];

    public static string Redact(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path ?? "";
        }

        foreach (var prefix in TokenPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.Length > prefix.Length)
            {
                // Alles na het token (bijv. een extra segment) valt ook weg: de route kent geen vervolg.
                return $"{path[..prefix.Length]}{{token}}";
            }
        }

        return path;
    }

    /// <summary>Configureert de ASP.NET Core-tracing (Azure Monitor): <c>url.path</c> zonder token.</summary>
    public static IServiceCollection AddSensitivePathRedaction(this IServiceCollection services) =>
        services.Configure<OpenTelemetry.Instrumentation.AspNetCore.AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var enrich = options.EnrichWithHttpRequest;
            options.EnrichWithHttpRequest = (activity, request) =>
            {
                enrich?.Invoke(activity, request);
                var path = request.PathBase.Add(request.Path).Value;
                var redacted = Redact(path);
                if (!string.Equals(path, redacted, StringComparison.Ordinal))
                {
                    activity.SetTag("url.path", redacted);
                }
            };
        });
}

/// <summary>
/// Serilog: redigeert <c>RequestPath</c> (request logging en de scope van ASP.NET Core, op elk logbericht binnen een
/// verzoek) en <c>Path</c> (onverwachte fouten) met <see cref="SensitivePaths.Redact"/>.
/// </summary>
public sealed class SensitivePathEnricher : ILogEventEnricher
{
    private static readonly string[] PropertyNames = ["RequestPath", "Path"];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var name in PropertyNames)
        {
            if (logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string path }
                && SensitivePaths.Redact(path) is var redacted && !string.Equals(path, redacted, StringComparison.Ordinal))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(redacted)));
            }
        }
    }
}
