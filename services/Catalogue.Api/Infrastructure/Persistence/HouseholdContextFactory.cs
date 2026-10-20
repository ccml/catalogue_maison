using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Catalogue.Api.Infrastructure.Persistence;

/// <summary>
/// Construit un <see cref="CatalogueDbContext"/> pour un ménage désigné
/// <strong>explicitement</strong>, et non lu dans le jeton.
/// <para>
/// C'est le « chemin d'administration explicite » : il n'est emprunté que
/// par les endpoints de service (<see cref="ServiceEndpoint"/>) —
/// provisionnement et partage —, accessibles au seul compte de service de
/// Tenants.Api. Partout ailleurs, le contexte s'obtient par injection, et
/// donc par le claim <c>tenant</c>.
/// </para>
/// </summary>
public sealed class HouseholdContextFactory(
    TenantConnectionStringFactory connectionStrings,
    SchemaVersionRegistry versions)
{
    /// <summary>Contexte sur le schéma du ménage, sans aucune vérification de version.</summary>
    public CatalogueDbContext Create(TenantId household)
    {
        var options = new DbContextOptionsBuilder<CatalogueDbContext>()
            .UseNpgsql(connectionStrings.For(household), npgsql => npgsql
                .MigrationsHistoryTable(CatalogueDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CatalogueDbContext(options, household);
    }

    /// <summary>
    /// Contexte sur le schéma du ménage, ou <c>null</c> si ce schéma n'est pas
    /// à la version attendue — ou n'existe pas. Même règle que pour un membre
    /// du ménage : un schéma en retard ne se sert pas, il est un incident.
    /// </summary>
    public async Task<CatalogueDbContext?> OpenUpToDateAsync(TenantId household, CancellationToken cancellationToken)
    {
        var db = Create(household);

        if (versions.IsKnownUpToDate(household))
        {
            return db;
        }

        try
        {
            // Un schéma absent n'a pas de table d'historique : toutes les
            // migrations sont alors en attente, et il est refusé de même.
            if (!(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                versions.MarkUpToDate(household);
                return db;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await db.DisposeAsync();
            throw;
        }

        await db.DisposeAsync();
        return null;
    }
}
