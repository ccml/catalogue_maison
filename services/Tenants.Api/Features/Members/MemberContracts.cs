using Tenants.Api.Domain;

namespace Tenants.Api.Features.Members;

/// <summary>
/// Un membre du ménage, vu par son administrateur.
/// <para>
/// Ni le ménage ni le nom de schéma n'y figurent : le client n'a pas à les
/// connaître, ils sont implicites dans le jeton.
/// </para>
/// </summary>
/// <param name="Registered">
/// Faux pour un compte rattaché au ménage dans Keycloak mais absent du
/// registre — une incohérence affichée, jamais corrigée en silence.
/// </param>
/// <param name="IsSelf">Le compte de l'administrateur qui consulte.</param>
public sealed record MemberResponse(
    Guid Id,
    string Username,
    string? FirstName,
    string? LastName,
    MemberRole Role,
    bool IsAdmin,
    bool Enabled,
    bool Registered,
    bool IsSelf);

/// <summary>
/// Ajout d'un membre. Aucun champ ne désigne le ménage : c'est celui de
/// l'administrateur, lu dans son jeton.
/// </summary>
/// <param name="TemporaryPassword">
/// Transmis à Keycloak, jamais conservé ici. Keycloak exige d'en changer à
/// la première connexion.
/// </param>
public sealed record CreateMemberRequest(
    string? Username,
    string? FirstName,
    string? LastName,
    MemberRole? Role,
    bool IsAdmin,
    string? TemporaryPassword);

public sealed record UpdateMemberRoleRequest(MemberRole? Role, bool IsAdmin);

public sealed record ResetPasswordRequest(string? TemporaryPassword);

/// <summary>Écarts entre Keycloak et le registre, pour le ménage de l'appelant.</summary>
/// <param name="UnregisteredAccounts">Comptes rattachés au ménage dans Keycloak, absents du registre.</param>
/// <param name="OrphanEntries">Entrées du registre sans compte rattaché au ménage.</param>
public sealed record InconsistenciesResponse(
    IReadOnlyList<MemberResponse> UnregisteredAccounts,
    IReadOnlyList<OrphanEntryResponse> OrphanEntries);

public sealed record OrphanEntryResponse(
    Guid Subject,
    string Username,
    DateTimeOffset RegisteredAt,
    OrphanReason Reason);

public enum OrphanReason
{
    /// <summary>Le compte n'existe plus dans Keycloak.</summary>
    AccountMissing,

    /// <summary>Le compte existe, mais n'est plus rattaché à ce ménage.</summary>
    AccountDetached
}
