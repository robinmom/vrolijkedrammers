using Drammers.Infrastructure;
using Drammers.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests.Infrastructure;

public static class TestServices
{
    /// <summary>Databasediensten zoals in de API, met een instelbare klok.</summary>
    public static ServiceProvider Create(string connectionString, IClock clock, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddDrammersDatabase(connectionString);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
