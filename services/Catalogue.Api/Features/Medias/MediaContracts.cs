using Catalogue.Api.Domain;

namespace Catalogue.Api.Features.Medias;

/// <summary>Fiche réduite, pour les listes.</summary>
public sealed record MediaSummaryResponse(
    Guid Id,
    MediaType Type,
    string Title,
    string? Creator,
    int? Year,
    string? Location,
    bool IsLent,
    DateTimeOffset AddedAt);

/// <summary>Fiche complète, spécificités incluses.</summary>
public sealed record MediaDetailResponse(
    Guid Id,
    MediaType Type,
    string Title,
    string? Creator,
    int? Year,
    string? Location,
    bool IsLent,
    DateTimeOffset AddedAt,
    MediaSpecifics? Specifics);

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

public sealed record SetLentRequest(bool IsLent);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int Size);
