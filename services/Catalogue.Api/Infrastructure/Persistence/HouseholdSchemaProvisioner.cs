using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogue.Api.Infrastructure.Persistence;

/// <summary>
/// Crée le schéma d'un ménage et y joue toutes les migrations. Il passe par
/// le « chemin d'administration explicite » (<see cref="HouseholdContextFactory"/>) :
/// le ménage ne vient pas du jeton.
/// <para>
/// Idempotent : rejoué sur un schéma existant, il n'applique que les
/// migrations manquantes. Les deux étapes passent par les mêmes chemins que
/// <c>tools/dev.sh provision</c> — schéma, puis migrations rejouées sous son
/// <c>search_path</c> — et jamais par une création de tables depuis le modèle.
/// </para>
/// </summary>
public sealed class HouseholdSchemaProvisioner(
    IConfiguration configuration,
    TenantConnectionStringFactory connectionStrings,
    SchemaVersionRegistry versions,
    HouseholdContextFactory contexts,
    ILogger<HouseholdSchemaProvisioner> logger)
{
    public async Task<int> ProvisionAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var baseConnectionString = configuration.GetConnectionString(TenantConnectionStringFactory.ConnectionName)
            ?? throw new InvalidOperationException(
                $"Chaîne de connexion « {TenantConnectionStringFactory.ConnectionName} » absente de la configuration.");

        await using (var connection = new NpgsqlConnection(baseConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            // Le nom a passé la validation de TenantId ; la citation protège
            // du repli en minuscules de PostgreSQL.
            command.CommandText = $"CREATE SCHEMA IF NOT EXISTS \"{tenant.Value}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var db = contexts.Create(tenant);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
        await db.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Schéma du ménage {Tenant} provisionné : {Count} migration(s) appliquée(s).", tenant.Value, pending);
        return pending;
    }

    /// <summary>
    /// Supprime le schéma d'un ménage et <strong>tout</strong> son catalogue.
    /// Irréversible. Idempotent : un schéma déjà absent n'est pas une erreur.
    /// <para>
    /// Ce service ne décide pas qu'un ménage doit disparaître : Tenants.Api
    /// ne l'appelle que pour un ménage archivé, dont les comptes sont déjà
    /// désactivés.
    /// </para>
    /// </summary>
    public async Task<bool> DropAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var baseConnectionString = configuration.GetConnectionString(TenantConnectionStringFactory.ConnectionName)
            ?? throw new InvalidOperationException(
                $"Chaîne de connexion « {TenantConnectionStringFactory.ConnectionName} » absente de la configuration.");

        bool existed;
        await using (var connection = new NpgsqlConnection(baseConnectionString))
        {
            await connection.OpenAsync(cancellationToken);

            await using (var exists = connection.CreateCommand())
            {
                exists.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @name)";
                exists.Parameters.AddWithValue("name", tenant.Value);
                existed = (bool)(await exists.ExecuteScalarAsync(cancellationToken))!;
            }

            await using var drop = connection.CreateCommand();
            // Le nom a passé la validation de TenantId, qui refuse « public »
            // et les schémas système ; la citation protège du repli en
            // minuscules de PostgreSQL.
            drop.CommandText = $"DROP SCHEMA IF EXISTS \"{tenant.Value}\" CASCADE";
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        // Plus rien ne doit croire ce schéma à jour, ni garder de connexion
        // ouverte dessus : un ménage recréé sous le même nom repartirait
        // proprement.
        versions.Forget(tenant);
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionStrings.For(tenant)));

        logger.LogWarning("Schéma du ménage {Tenant} supprimé (existait : {Existed}).", tenant.Value, existed);
        return existed;
    }
}
