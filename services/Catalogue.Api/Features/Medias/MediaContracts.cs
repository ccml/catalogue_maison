using Catalogue.Api.Domain;

namespace Catalogue.Api.Features.Medias;

/// <summary>Fiche réduite, pour les listes.</summary>
/// <param name="LentTo">Emprunteur du prêt en cours, quand <paramref name="IsLent"/>.</param>
public sealed record MediaSummaryResponse(
    Guid Id,
    MediaType Type,
    string Title,
    string? Creator,
    int? Year,
    string? Location,
    bool IsLent,
    DateTimeOffset AddedAt,
    string? LentTo = null);

/// <summary>Fiche complète, spécificités incluses.</summary>
/// <param name="LentTo">Emprunteur du prêt en cours, quand <paramref name="IsLent"/>.</param>
public sealed record MediaDetailResponse(
    Guid Id,
    MediaType Type,
    string Title,
    string? Creator,
    int? Year,
    string? Location,
    bool IsLent,
    DateTimeOffset AddedAt,
    MediaSpecifics? Specifics,
    string? LentTo = null);

/// <summary>
/// Création d'un média. Seuls <see cref="Type"/> et <see cref="Title"/> sont
/// requis : la saisie manuelle doit rester possible sans rien connaître
/// d'autre du média.
/// </summary>
public sealed record CreateMediaRequest(
    MediaType Type,
    string? Title,
    string? Creator = null,
    int? Year = null,
    string? Location = null,
    MediaSpecifics? Specifics = null);

/// <summary>
/// Correction d'une fiche existante. Le <see cref="Type"/> en fait partie :
/// prendre un album de BD pour un livre doit se réparer sans resaisir.
/// </summary>
public sealed record UpdateMediaRequest(
    MediaType Type,
    string? Title,
    string? Creator = null,
    int? Year = null,
    string? Location = null,
    MediaSpecifics? Specifics = null);

/// <summary>
/// Prêt ou retour saisi par le ménage lui-même. Rejoué dans le même sens, ne
/// fait rien.
/// </summary>
/// <param name="BorrowerName">À qui le média est prêté ; « Inconnu » s'il est omis.</param>
public sealed record SetLentRequest(bool IsLent, string? BorrowerName = null);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int Size);

/// <summary>Ordre de la liste du catalogue.</summary>
public enum MediaSort
{
    /// <summary>Derniers ajoutés d'abord : ce qu'on vient de saisir.</summary>
    Recent,

    /// <summary>Par titre.</summary>
    Title,

    /// <summary>Par auteur, réalisateur ou artiste, puis par titre.</summary>
    Creator,

    /// <summary>Par série, puis par tome : l'ordre d'une étagère de BD.</summary>
    Series
}
