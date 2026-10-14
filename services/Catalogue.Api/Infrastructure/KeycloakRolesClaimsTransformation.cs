using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace Catalogue.Api.Infrastructure;

/// <summary>
/// Keycloak place les rôles de realm dans le claim composite
/// <c>realm_access</c>. ASP.NET Core attend des claims de rôle plats : cette
/// transformation les aplatit, sans rien accorder qui ne soit dans le jeton.
/// </summary>
public sealed class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    private const string RealmAccessClaim = "realm_access";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        var realmAccess = identity.FindFirst(RealmAccessClaim)?.Value;
        if (string.IsNullOrWhiteSpace(realmAccess))
        {
            return Task.FromResult(principal);
        }

        foreach (var role in ReadRoles(realmAccess))
        {
            if (!identity.HasClaim(identity.RoleClaimType, role))
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, role));
            }
        }

        return Task.FromResult(principal);
    }

    private static IEnumerable<string> ReadRoles(string realmAccess)
    {
        List<string> roles = [];
        try
        {
            using var document = JsonDocument.Parse(realmAccess);
            if (document.RootElement.TryGetProperty("roles", out var array)
                && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } role)
                    {
                        roles.Add(role);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Un claim illisible n'accorde aucun rôle : on ne devine pas.
        }

        return roles;
    }
}
