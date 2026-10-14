using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Catalogue.Api.Infrastructure.Persistence;

/// <summary>
/// Utilisé par <c>dotnet ef</c>. Le chemin d'administration est le seul
/// endroit où un ménage est désigné explicitement, hors jeton.
/// <para>
/// <c>migrations add</c> ne touche pas la base : le ménage n'y a aucune
/// importance, et les migrations produites sont neutres. <c>database
/// update</c>, lui, applique les migrations au schéma désigné par
/// <c>CATALOGUE_TENANT</c> — voir <c>tools/dev.sh provision</c> et
/// <c>migrate-all</c>.
/// </para>
/// </summary>
public sealed class CatalogueDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogueDbContext>
{
    private const string DefaultTenant = "migration_template";

    public CatalogueDbContext CreateDbContext(string[] args)
    {
        var tenant = TenantId.Create(Environment.GetEnvironmentVariable("CATALOGUE_TENANT") ?? DefaultTenant);

        var baseConnectionString = Environment.GetEnvironmentVariable("CATALOGUE_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=catalogue;Username=maison;Password=dev";

        var connectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = tenant.Value
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<CatalogueDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(CatalogueDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CatalogueDbContext(options, tenant);
    }
}
