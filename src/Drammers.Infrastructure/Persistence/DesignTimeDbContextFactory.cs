using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Drammers.Infrastructure.Persistence;

/// <summary>Voor <c>dotnet ef</c> (migraties en scripts); maakt geen verbinding.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DrammersDbContext>
{
    public DrammersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DrammersDbContext>()
            .UseSqlServer("Server=design-time;Database=design-time")
            .Options);
}
