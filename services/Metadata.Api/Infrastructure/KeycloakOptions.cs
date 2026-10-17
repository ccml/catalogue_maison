namespace Metadata.Api.Infrastructure;

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
    /// Faux uniquement pour un Keycloak joint en HTTP : le Keycloak local, ou
    /// l'adresse interne <see cref="InternalAuthority"/> d'un déploiement,
    /// sur le réseau privé des conteneurs. N'autorise jamais autre chose que
    /// du HTTPS ailleurs.
    /// </summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>
    /// Adresse du <strong>même realm</strong> sur le réseau interne d'un
    /// déploiement, par ex. <c>http://cataloguemaison-keycloak:8080/realms/maison</c>.
    /// <para>
    /// Le service y lit la découverte et les clés de signature, et y demande
    /// ses jetons de service, sans repasser par Internet et le proxy TLS.
    /// L'issuer attendu reste <see cref="Authority"/> : Keycloak, configuré
    /// avec son nom d'hôte public, émet ce même issuer quel que soit le
    /// chemin par lequel on l'appelle. Vide en développement.
    /// </para>
    /// </summary>
    public string? InternalAuthority { get; init; }

    /// <summary>Base des appels de ce service à Keycloak : l'adresse interne si elle est configurée.</summary>
    public string BackchannelAuthority =>
        string.IsNullOrWhiteSpace(InternalAuthority) ? Authority : InternalAuthority;
}
