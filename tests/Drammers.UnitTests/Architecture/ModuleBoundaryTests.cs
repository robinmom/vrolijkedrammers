using System.Reflection;
using Drammers.Modules.Audit;
using Drammers.Modules.Content;
using Drammers.Modules.Identity;
using Drammers.Modules.Import;
using Drammers.Modules.Membership;
using Drammers.Modules.Notification;
using Drammers.Modules.Parade;
using Drammers.Modules.Payments;
using Drammers.Modules.Ticketing;
using Drammers.SharedKernel.Time;
using Drammers.Worker;
using NetArchTest.Rules;

namespace Drammers.UnitTests.Architecture;

/// <summary>
/// Bewaakt de modulegrenzen van de modulaire monoliet (docs/03 §5, ADR-002, ADR-007).
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(AuditModule).Assembly,
        typeof(ContentModule).Assembly,
        typeof(IdentityModule).Assembly,
        typeof(ImportModule).Assembly,
        typeof(MembershipModule).Assembly,
        typeof(NotificationModule).Assembly,
        typeof(ParadeModule).Assembly,
        typeof(PaymentsModule).Assembly,
        typeof(TicketingModule).Assembly,
    ];

    public static TheoryData<string> ModuleNames() =>
        new(ModuleAssemblies.Select(a => a.GetName().Name!));

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_is_niet_afhankelijk_van_andere_modules(string moduleName)
    {
        var module = ModuleAssemblies.Single(a => a.GetName().Name == moduleName);
        var otherModules = ModuleAssemblies
            .Where(a => a != module)
            .Select(a => a.GetName().Name!)
            .ToArray();

        var result = Types.InAssembly(module)
            .ShouldNot()
            .HaveDependencyOnAny(otherModules)
            .GetResult();

        Assert.True(result.IsSuccessful, $"{moduleName} verwijst naar een andere module: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void SharedKernel_is_niet_afhankelijk_van_andere_Drammers_projecten()
    {
        var result = Types.InAssembly(typeof(IClock).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Drammers.Api", "Drammers.Infrastructure", "Drammers.Worker", "Drammers.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Worker_is_niet_afhankelijk_van_AspNetCore()
    {
        var result = Types.InAssembly(typeof(WorkerModule).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }
}
