namespace Metadata.Api.Infrastructure.Sources;

/// <summary>
/// Sources externes et politique de cache. Toutes les sources retenues sont
/// gratuites et sans clé : en ajouter une qui en exige une est une décision
/// à prendre explicitement (voir CLAUDE.md).
/// </summary>
public sealed class EnrichmentOptions
{
    public const string Section = "Enrichment";

    /// <summary>Catalogue général de la BnF, interface SRU. Source principale des livres et BD.</summary>
    public Uri BnfBaseUrl { get; init; } = new("https://catalogue.bnf.fr/api/");

    /// <summary>Open Library, en repli de la BnF pour les ouvrages qu'elle ne connaît pas.</summary>
    public Uri OpenLibraryBaseUrl { get; init; } = new("https://openlibrary.org/");

    /// <summary>MusicBrainz, pour les codes-barres de disques.</summary>
    public Uri MusicBrainzBaseUrl { get; init; } = new("https://musicbrainz.org/ws/2/");

    /// <summary>
    /// MusicBrainz refuse les clients anonymes : il exige un User-Agent qui
    /// nomme l'application et, idéalement, un moyen de contact.
    /// </summary>
    public string MusicBrainzUserAgent { get; init; } = "CatalogueMaison/0.1 (developpement local)";

    /// <summary>OMDb, pour la recherche de films par leur titre.</summary>
    public Uri OmdbBaseUrl { get; init; } = new("https://www.omdbapi.com/");

    /// <summary>
    /// Clé d'API OMDb. <strong>Secret</strong> : jamais dans le dépôt ni dans
    /// <c>appsettings.json</c>, fournie par les user-secrets ou
    /// l'environnement (<c>Enrichment__OmdbApiKey</c>). Sans elle, la
    /// recherche de films est simplement désactivée.
    /// </summary>
    public string? OmdbApiKey { get; init; }

    /// <summary>Vrai si la recherche de films est utilisable.</summary>
    public bool FilmSearchEnabled => !string.IsNullOrWhiteSpace(OmdbApiKey);

    /// <summary>Délai accordé à chaque appel externe.</summary>
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>Durée de validité d'une notice trouvée.</summary>
    public int FoundTtlDays { get; init; } = 180;

    /// <summary>
    /// Durée pendant laquelle un code inconnu de toutes les sources n'est pas
    /// redemandé. Courte : les catalogues s'enrichissent, une nouveauté
    /// absente aujourd'hui peut être décrite le mois prochain.
    /// </summary>
    public int NotFoundTtlDays { get; init; } = 7;

    /// <summary>
    /// Durée de validité d'une notice provisoire. Courte, pour la même
    /// raison : la notice définitive, plus complète, la remplace à la BnF
    /// dans les semaines qui suivent la parution.
    /// </summary>
    public int ProvisionalTtlDays { get; init; } = 7;
}
