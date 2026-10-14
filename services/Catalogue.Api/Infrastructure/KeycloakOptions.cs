namespace Catalogue.Api.Infrastructure;

/// <summary>
/// Configuration du fournisseur d'identité. Aucun secret ici : ce service
/// ne fait que <em>valider</em> des jetons émis par Keycloak.
/// </summary>
public sealed class KeycloakOptions
{
    public const string Section = "Keycloak";

    /// <summary>URL du realm, qui sert aussi d'<c>issuer</c> attendu.</summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>Audience attendue dans le jeton : l'identifiant de ce client.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Faux uniquement pour le Keycloak local en HTTP. N'autorise jamais
    /// autre chose que du HTTPS ailleurs.
    /// </summary>
    public bool RequireHttpsMetadata { get; init; } = true;
}
