using Catalogue.Api.Domain;

namespace Catalogue.Api.Features.Sharing;

/// <summary>Ce qu'un ménage partenaire peut savoir de la disponibilité d'un média.</summary>
public enum SharedAvailability
{
    Available,

    /// <summary>Prêté — à qui, le ménage partenaire ne le sait pas.</summary>
    Lent,

    /// <summary>Prêté au ménage partenaire lui-même.</summary>
    LentToYou,

    /// <summary>Le ménage partenaire a une demande en attente sur ce média.</summary>
    RequestedByYou
}

/// <summary>
/// Fiche d'un média vue par un ménage partenaire. Ni emplacement, ni date
/// d'ajout, ni emprunteur : ce qui décrit l'œuvre, pas la maison.
/// </summary>
public sealed record SharedMediaResponse(
    Guid Id,
    MediaType Type,
    string Title,
    string? Creator,
    int? Year,
    string? Series,
    int? Volume,
    SharedAvailability Availability);

/// <summary>Fiche complète vue par un ménage partenaire : les spécificités décrivent l'œuvre.</summary>
public sealed record SharedMediaDetailResponse(
    Guid Id,
    MediaType Type,
    string Title,
    string? Creator,
    int? Year,
    SharedAvailability Availability,
    MediaSpecifics? Specifics);

/// <param name="BorrowerName">
/// Libellé du demandeur, composé par Tenants.Api — nom du ménage et de la
/// personne. Copié tel quel : ce schéma ne garde rien qui désigne un autre ménage.
/// </param>
public sealed record CreateLoanRequestRequest(string? BorrowerName);

/// <summary>Une demande ou un prêt, vu par le ménage partenaire qui l'a fait.</summary>
public sealed record SharedLoanResponse(
    Guid Id,
    Guid MediaId,
    MediaType MediaType,
    string MediaTitle,
    LoanStatus Status,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? LentAt,
    DateTimeOffset? ClosedAt);
