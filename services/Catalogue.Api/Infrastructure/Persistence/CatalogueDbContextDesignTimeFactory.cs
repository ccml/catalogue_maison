using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Catalogue.Api.Infrastructure.Persistence;

/// <summary>
/// Utilisé par <c>dotnet ef</c>. Le chemin d'administration est le seul
/// endroit où un ménage est désigné explicitement, hors jeton.
/// <para>
/// Le schéma indiqué ici ne sert qu'à <em>générer</em> la migration ;
/// l'appliquer se fait schéma par schéma, en itérant sur le registre des
/// ménages (voir <c>tools/dev.sh migrate-all</c>).
/// </para>
/// </summary>
public sealed class CatalogueDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogueDbContext>
{
    public CatalogueDbContext CreateDbContext(string[] args)
    {
        var tenantValue = Environment.GetEnvironmentVariable("CATALOGUE_TENANT") ?? "migration_template";
        var tenant = TenantId.Create(tenantValue);

        var connectionString = Environment.GetEnvironmentVariable("CATALOGUE_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=catalogue;Username=maison;Password=dev";

        var options = new DbContextOptionsBuilder<CatalogueDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(CatalogueDbContext.MigrationsHistoryTable, tenant.Value))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>()
            .Options;

        return new CatalogueDbContext(options, tenant);
    }
}
