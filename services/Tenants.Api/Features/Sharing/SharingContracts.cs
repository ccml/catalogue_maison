namespace Tenants.Api.Features.Sharing;

/// <summary>
/// Invitation tout juste créée. <see cref="Code"/> n'est rendu qu'ici : il
/// n'est pas stocké, et ne pourra plus être relu.
/// </summary>
public sealed record CreatedInvitationResponse(Guid Id, string Code, DateTimeOffset ExpiresAt);

/// <summary>Invitation en attente, sans son code.</summary>
public sealed record InvitationResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record AcceptInvitationRequest(string? Code);

/// <summary>
/// Un partage, vu de l'un ou l'autre côté. <see cref="HouseholdName"/> est le
/// nom affiché de l'autre ménage — jamais son identifiant, qui est un nom de
/// schéma.
/// </summary>
public sealed record ShareResponse(Guid Id, string HouseholdName, DateTimeOffset Since);
