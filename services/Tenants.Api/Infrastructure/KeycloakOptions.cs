namespace Tenants.Api.Infrastructure;

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

    /// <summary>
    /// Autres URL sous lesquelles <strong>le même realm</strong> peut être
    /// joint, et donc autres <c>issuer</c> acceptés. Keycloak tire l'issuer
    /// de l'URL par laquelle on l'appelle : à travers le relais HTTPS du
    /// frontend (<c>tools/dev.sh front-lan</c>), un jeton porte l'origine vue
    /// par le téléphone, pas <see cref="Authority"/>.
    /// <para>
    /// Ce n'est pas un assouplissement : l'issuer reste vérifié contre une
    /// liste fermée, la signature contre les clés du realm. Vide par défaut,
    /// renseigné par l'environnement — jamais en dur, l'adresse d'un poste
    /// n'a rien à faire dans le dépôt.
    /// </para>
    /// </summary>
    public string[] AdditionalIssuers { get; init; } = [];
}
