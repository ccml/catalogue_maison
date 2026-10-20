using Catalogue.Api.Domain;

namespace Catalogue.Api.Features.Loans;

/// <summary>
/// Un prêt ou une demande, vu par le ménage prêteur.
/// </summary>
/// <param name="FromSharedHousehold">
/// Vrai pour une demande arrivée par un partage. L'identifiant du partage
/// n'est pas exposé : il ne sert qu'aux échanges entre services.
/// </param>
public sealed record LoanResponse(
    Guid Id,
    Guid MediaId,
    MediaType MediaType,
    string MediaTitle,
    LoanStatus Status,
    string BorrowerName,
    bool FromSharedHousehold,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? LentAt,
    DateTimeOffset? ClosedAt);
