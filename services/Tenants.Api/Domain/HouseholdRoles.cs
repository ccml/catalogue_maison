namespace Tenants.Api.Domain;

/// <summary>Place d'une personne dans son ménage. Exactement une des deux.</summary>
public enum MemberRole
{
    Adult,
    Child
}

/// <summary>
/// Rôles de realm Keycloak, versionnés dans <c>/keycloak</c>.
/// <para>
/// Ce sont les <strong>seuls</strong> que ce service attribue ou retire. Le
/// compte de service voit tous les rôles du realm, <c>enrichissement</c>
/// compris : la liste fermée ci-dessous est ce qui empêche un administrateur
/// de ménage d'accorder à une personne un rôle réservé aux services.
/// </para>
/// </summary>
public static class Roles
{
    public const string Adult = "adulte";
    public const string Child = "enfant";
    public const string HouseholdAdmin = "admin-menage";

    /// <summary>
    /// Crée et supprime les ménages. Porté par un compte opérateur
    /// <em>sans</em> ménage, attribué par l'import du realm uniquement :
    /// volontairement absent de <see cref="Managed"/>, aucun administrateur de
    /// ménage ne peut l'accorder.
    /// </summary>
    public const string PlatformAdmin = "admin-plateforme";

    public static readonly IReadOnlySet<string> Managed =
        new HashSet<string>(StringComparer.Ordinal) { Adult, Child, HouseholdAdmin };

    /// <summary>Rôles de realm qui traduisent une place dans le ménage.</summary>
    public static IReadOnlySet<string> For(MemberRole role, bool isAdmin)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal)
        {
            role == MemberRole.Adult ? Adult : Child
        };

        if (isAdmin)
        {
            roles.Add(HouseholdAdmin);
        }

        return roles;
    }

    /// <summary>
    /// Lecture inverse. Un compte qui ne porte ni <c>adulte</c> ni
    /// <c>enfant</c> est présenté comme enfant : c'est la place qui accorde le
    /// moins, et l'administrateur la voit et peut la corriger.
    /// </summary>
    public static MemberRole RoleOf(IReadOnlySet<string> realmRoles) =>
        realmRoles.Contains(Adult) ? MemberRole.Adult : MemberRole.Child;
}
