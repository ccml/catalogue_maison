using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;

namespace Catalogue.Api.Features.Scans;

/// <summary>Ce que l'enrichissement a pu dire du code.</summary>
public enum EnrichmentStatus
{
    /// <summary>Une notice a été trouvée et a servi à pré-remplir la fiche.</summary>
    Found,

    /// <summary>Aucune source ne connaît ce code.</summary>
    NotFound,

    /// <summary>L'enrichissement n'a pas pu répondre. La saisie reste possible.</summary>
    Unavailable,

    /// <summary>Le média est déjà au catalogue du ménage : rien n'a été demandé.</summary>
    NotAttempted
}

/// <summary>
/// Fiche pré-remplie, à valider ou corriger par l'utilisateur avant
/// enregistrement. Même forme que <see cref="CreateMediaRequest"/>, sans
/// l'emplacement que seul le ménage connaît. <strong>Rien n'est
/// enregistré</strong> à ce stade.
/// </summary>
public sealed record ScanDraft(
    MediaType Type,
    string? Title,
    string? Creator,
    int? Year,
    MediaSpecifics? Specifics);

/// <summary>
/// Résultat d'un scan.
/// </summary>
/// <param name="Code">Code tel que normalisé.</param>
/// <param name="Existing">Média du ménage portant déjà ce code, s'il y en a un.</param>
/// <param name="Draft">Fiche pré-remplie, absente si le média existe déjà ou si rien n'est connu du code.</param>
/// <param name="Enrichment">Ce que l'enrichissement a pu faire.</param>
/// <param name="Source">Identifiant de la source de la notice (<c>bnf</c>, <c>openlibrary</c>, <c>musicbrainz</c>).</param>
public sealed record ScanResponse(
    string Code,
    MediaSummaryResponse? Existing,
    ScanDraft? Draft,
    EnrichmentStatus Enrichment,
    string? Source = null);
