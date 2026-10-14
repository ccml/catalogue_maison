using System.Collections.Concurrent;
using Npgsql;

namespace Catalogue.Api.Infrastructure.Tenancy;

/// <summary>
/// Construit la chaîne de connexion d'un ménage en y fixant son
/// <c>search_path</c>.
/// <para>
/// Le schéma est porté par la chaîne de connexion, et non par le modèle EF.
/// Deux raisons :
/// </para>
/// <list type="number">
///   <item>
///     Une migration EF générée depuis un modèle à schéma explicite inscrit
///     ce nom de schéma <strong>en dur</strong> dans le fichier généré. La
///     rejouer sur un autre ménage créerait les tables dans le schéma
///     d'origine. Un modèle sans schéma produit des migrations neutres,
///     rejouables sur tous les ménages — ce que le projet exige.
///   </item>
///   <item>
///     Le <c>search_path</c> dans la chaîne de connexion n'est pas de l'état
///     de connexion mutable : chaque chaîne a son propre pool, et une
///     connexion de ce pool ne peut pas changer de ménage. Ce serait faux
///     d'un <c>SET search_path</c> émis après l'ouverture, qui se perdrait au
///     recyclage de la connexion.
///   </item>
/// </list>
/// <para>
/// Conséquence assumée : un pool par ménage. À l'échelle d'un foyer, c'est
/// négligeable ; ce serait à réexaminer avec un grand nombre de ménages.
/// </para>
/// </summary>
public sealed class TenantConnectionStringFactory(IConfiguration configuration)
{
    public const string ConnectionName = "Catalogue";

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    public string For(TenantId tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return _cache.GetOrAdd(tenant.Value, searchPath =>
        {
            var baseConnectionString = configuration.GetConnectionString(ConnectionName)
                ?? throw new InvalidOperationException(
                    $"Chaîne de connexion « {ConnectionName} » absente de la configuration.");

            // La valeur a déjà passé la validation de TenantId ; le builder
            // s'occupe en plus de l'échappement.
            return new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                SearchPath = searchPath
            }.ConnectionString;
        });
    }
}
