namespace Tenants.Api.Infrastructure;

/// <summary>
/// Policies d'autorisation. L'interface n'en tire aucune règle : toute
/// autorisation est revalidée ici, à chaque requête.
/// </summary>
public static class Policies
{
    /// <summary>Appartenir à un ménage exploitable. Socle de tout accès.</summary>
    public const string HouseholdMember = "household-member";

    /// <summary>Administrer les membres de <em>son</em> ménage, et d'aucun autre.</summary>
    public const string HouseholdAdmin = "household-admin";

    /// <summary>
    /// Créer et supprimer des ménages. Exige le rôle de plateforme
    /// <strong>et l'absence</strong> de claim <c>tenant</c> : l'opérateur
    /// n'appartient à aucun ménage, et un membre ne cumule pas les deux
    /// casquettes.
    /// </summary>
    public const string PlatformAdmin = "platform-admin";
}
