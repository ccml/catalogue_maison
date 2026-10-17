using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Infrastructure.Keycloak;

/// <summary>
/// Les comptes, tels que Keycloak les tient. Seule porte de ce service vers
/// l'Admin REST API ; remplacée par un annuaire en mémoire dans les tests.
/// </summary>
public interface IIdentityDirectory
{
    /// <summary>Comptes dont l'attribut <c>tenant</c> vaut exactement ce ménage.</summary>
    Task<IReadOnlyList<DirectoryAccount>> ListByTenantAsync(TenantId tenant, CancellationToken cancellationToken);

    Task<DirectoryAccount?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<DirectoryAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Crée le compte, attribut <c>tenant</c> et mot de passe temporaire compris.</summary>
    /// <exception cref="UsernameTakenException">Identifiant déjà pris dans le realm.</exception>
    /// <exception cref="PasswordRejectedException">Mot de passe refusé par la politique du realm.</exception>
    Task<Guid> CreateAsync(NewAccount account, CancellationToken cancellationToken);

    /// <summary>Noms des rôles de realm attribués directement au compte.</summary>
    Task<IReadOnlySet<string>> GetRealmRolesAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Aligne les rôles <see cref="Domain.Roles.Managed"/> du compte sur
    /// <paramref name="wanted"/>. Tout autre rôle est laissé tel quel.
    /// </summary>
    Task SetManagedRolesAsync(Guid id, IReadOnlySet<string> wanted, CancellationToken cancellationToken);

    /// <exception cref="PasswordRejectedException">Mot de passe refusé par la politique du realm.</exception>
    Task SetTemporaryPasswordAsync(Guid id, string password, CancellationToken cancellationToken);

    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);

    /// <summary>Révoque toutes les sessions du compte.</summary>
    Task SignOutEverywhereAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Supprime le compte. Idempotent : un compte déjà absent n'est pas une erreur.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record DirectoryAccount(
    Guid Id,
    string Username,
    string? FirstName,
    string? LastName,
    bool Enabled,
    string? Tenant);

public sealed record NewAccount(
    string Username,
    string FirstName,
    string LastName,
    TenantId Tenant,
    string TemporaryPassword);

/// <summary>Keycloak injoignable, ou réponse inattendue.</summary>
public class IdentityDirectoryException(string message) : Exception(message);

public sealed class UsernameTakenException(string username)
    : IdentityDirectoryException($"L'identifiant « {username} » est déjà utilisé.");

public sealed class PasswordRejectedException(string? reason)
    : IdentityDirectoryException(reason ?? "Mot de passe refusé par Keycloak.");
