using Drammers.Api.ErrorHandling;
using Drammers.SharedKernel.Time;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

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
    builder.Services.AddHealthChecks();
    builder.Services.AddSingleton<IClock, SystemClock>();

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
    else
    {
        app.UseHsts();
    }

    app.MapHealthChecks("/health/live");
    app.MapControllers();

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
