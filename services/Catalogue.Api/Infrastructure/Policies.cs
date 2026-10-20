namespace Catalogue.Api.Infrastructure;

/// <summary>
/// Policies d'autorisation. L'interface n'en tire aucune règle : toute
/// autorisation est revalidée ici, à chaque requête.
/// </summary>
public static class Policies
{
    /// <summary>Appartenir à un ménage exploitable. Socle de tout accès.</summary>
    public const string HouseholdMember = "household-member";

    /// <summary>Écrire dans le catalogue du ménage.</summary>
    public const string CatalogueWrite = "catalogue-write";

    /// <summary>
    /// Créer le schéma d'un ménage. Réservé au compte de service de
    /// Tenants.Api : rôle de provisionnement, et <strong>aucun</strong> claim
    /// <c>tenant</c>.
    /// </summary>
    public const string Provisioning = "provisioning";

    /// <summary>
    /// Lire le catalogue d'un ménage pour un ménage partenaire, et y déposer
    /// des demandes de prêt. Réservé au compte de service de Tenants.Api,
    /// qui a vérifié le partage : rôle de partage, et <strong>aucun</strong>
    /// claim <c>tenant</c>.
    /// </summary>
    public const string Sharing = "sharing";
}

/// <summary>Rôles de realm Keycloak, versionnés dans <c>/keycloak</c>.</summary>
public static class Roles
{
    public const string Adult = "adulte";
    public const string Child = "enfant";
    public const string HouseholdAdmin = "admin-menage";

    /// <summary>Attribué au seul compte de service de Tenants.Api, jamais à une personne.</summary>
    public const string Provisioning = "provisionnement";

    /// <summary>Attribué au seul compte de service de Tenants.Api, jamais à une personne.</summary>
    public const string Sharing = "partage";
}
