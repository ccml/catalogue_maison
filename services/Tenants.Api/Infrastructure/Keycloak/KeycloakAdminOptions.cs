namespace Tenants.Api.Infrastructure.Keycloak;

/// <summary>
/// Accès à l'Admin REST API de Keycloak. Tenants.Api est le
/// <strong>seul</strong> service à y avoir accès : point d'entrée unique de
/// l'administration d'un ménage (inversion d'écriture).
/// </summary>
public sealed class KeycloakAdminOptions
{
    public const string Section = "KeycloakAdmin";

    /// <summary>Racine de l'API d'administration du realm, par ex. <c>…/admin/realms/maison/</c>.</summary>
    public Uri? BaseUrl { get; init; }

    /// <summary>Client confidentiel de ce service dans le realm.</summary>
    public string ClientId { get; init; } = "tenants-api";

    /// <summary>
    /// Secret du client. Jamais dans le code ni dans <c>appsettings.json</c> :
    /// fourni par l'environnement (<c>KeycloakAdmin__ClientSecret</c>), ou
    /// par <c>appsettings.Development.json</c> pour le seul Keycloak local.
    /// </summary>
    public string? ClientSecret { get; init; }

    public int TimeoutSeconds { get; init; } = 10;
}
