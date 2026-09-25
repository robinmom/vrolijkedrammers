using Azure.Monitor.OpenTelemetry.AspNetCore;
using Drammers.Api;
using Drammers.Api.Authentication;
using Drammers.Api.Authorization;
using Drammers.Api.ErrorHandling;
using Drammers.Api.Portal;
using Drammers.Infrastructure;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    // Geen reloadable bootstraplogger: die kan maar één keer worden bevroren (meerdere testhosts in één proces).
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Customize);
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddControllers();
    builder.Services.AddOpenApi("v1");
    builder.Services.AddDrammersInfrastructure(builder.Configuration, builder.Environment);
    builder.Services.AddDrammersAuthentication(builder.Configuration);
    builder.Services.AddDrammersAuthorization();
    builder.Services.AddSingleton<IClock, SystemClock>();

    // Traces, metrics en logs naar Application Insights; de connection string zet Bicep (niet geheim).
    if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    {
        builder.Services.AddOpenTelemetry().UseAzureMonitor();
    }

    var app = builder.Build();

    app.UseForwardedHeaders();
    app.UseSecurityHeaders();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
    }
    else
    {
        app.UseHsts();
    }

    // Het portal (statische bestanden) vóór de authenticatie: het is publiek en logt zelf in via MSAL.
    app.UsePortalStaticFiles();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    // live: het proces draait (App Service health check). ready: SQL, Key Vault en Blob zijn bereikbaar.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = AppVersion.WriteLiveResponseAsync })
        .AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(DependencyInjection.ReadyTag) })
        .AllowAnonymous();
    app.MapControllers();
    app.MapPortalFallback();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "De API is onverwacht gestopt");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Toegankelijk gemaakt voor <c>WebApplicationFactory</c> in de API-tests.</summary>
public partial class Program;
