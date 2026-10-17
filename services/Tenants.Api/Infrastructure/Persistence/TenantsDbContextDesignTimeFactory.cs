using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tenants.Api.Infrastructure.Persistence;

/// <summary>
/// Utilisé par <c>dotnet ef</c>. La base est désignée par
/// <c>TENANTS_CONNECTION</c>, ou par la base locale de développement.
/// </summary>
public sealed class TenantsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantsDbContext>
{
    public TenantsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TENANTS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=tenants;Username=maison;Password=dev";

        var options = new DbContextOptionsBuilder<TenantsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(TenantsDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TenantsDbContext(options);
    }
}
