namespace Metadata.Api.Infrastructure;

/// <summary>Policies d'autorisation de Metadata.Api.</summary>
public static class Policies
{
    /// <summary>
    /// Interroger l'enrichissement. Réservé aux services : un jeton
    /// d'utilisateur ne porte ni l'audience <c>metadata-api</c> ni ce rôle.
    /// Le frontend passe par Catalogue.Api, qui décide de ce qu'il conserve.
    /// </summary>
    public const string MetadataRead = "metadata-read";
}

/// <summary>Rôles de realm Keycloak, versionnés dans <c>/keycloak</c>.</summary>
public static class Roles
{
    /// <summary>Attribué au seul compte de service de <c>catalogue-api</c>.</summary>
    public const string Enrichment = "enrichissement";
}
