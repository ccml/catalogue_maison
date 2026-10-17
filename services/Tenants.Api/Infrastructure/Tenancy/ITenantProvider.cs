namespace Tenants.Api.Infrastructure.Tenancy;

/// <summary>
/// Donne le ménage de la requête courante. Unique source : le claim
/// <c>tenant</c> du jeton.
/// </summary>
public interface ITenantProvider
{
    /// <summary>
    /// Ménage de la requête.
    /// </summary>
    /// <exception cref="TenantUnresolvedException">
    /// Si le jeton ne porte pas de ménage exploitable. Jamais de ménage par
    /// défaut, jamais de repli.
    /// </exception>
    TenantId Current { get; }

    bool TryGetCurrent(out TenantId? tenant);
}

/// <summary>Jeton sans ménage exploitable : la requête ne peut pas être servie.</summary>
public sealed class TenantUnresolvedException()
    : Exception("Le jeton ne porte pas de claim « tenant » exploitable.");
