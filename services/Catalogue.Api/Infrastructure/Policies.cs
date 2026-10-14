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
}

/// <summary>Rôles de realm Keycloak, versionnés dans <c>/keycloak</c>.</summary>
public static class Roles
{
    public const string Adult = "adulte";
    public const string Child = "enfant";
    public const string HouseholdAdmin = "admin-menage";
}
