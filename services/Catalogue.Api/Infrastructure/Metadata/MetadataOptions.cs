namespace Catalogue.Api.Infrastructure.Metadata;

/// <summary>
/// Accès à Metadata.Api, en service à service. Catalogue.Api s'y présente
/// avec <strong>son propre</strong> jeton, obtenu en client credentials :
/// le jeton de l'utilisateur n'est pas relayé, l'enrichissement n'agissant
/// au nom de personne.
/// </summary>
public sealed class MetadataOptions
{
    public const string Section = "Metadata";

    /// <summary>Origine de Metadata.Api.</summary>
    public Uri? BaseUrl { get; init; }

    /// <summary>Client confidentiel de ce service dans le realm.</summary>
    public string ClientId { get; init; } = "catalogue-api";

    /// <summary>
    /// Secret du client. Jamais dans le code ni dans <c>appsettings.json</c> :
    /// fourni par l'environnement (<c>Metadata__ClientSecret</c>), ou par
    /// <c>appsettings.Development.json</c> pour le seul Keycloak local.
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Délai total accordé à l'enrichissement. Au-delà, le scan rend la main
    /// sans notice : l'échec de l'enrichissement n'empêche jamais l'ajout.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 12;
}
