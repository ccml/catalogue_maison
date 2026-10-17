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
}
