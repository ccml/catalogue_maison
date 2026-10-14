using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Catalogue.Api.Tests;

/// <summary>
/// Émet des jetons de test signés par une clé locale.
/// <para>
/// Aucune validation n'est désactivée : issuer, audience, signature et durée
/// de vie restent vérifiés par l'API. Seule la <em>provenance</em> de la clé
/// change — un test ne doit pas dépendre d'un Keycloak joignable sur le
/// réseau, mais il doit emprunter exactement le même chemin de validation
/// qu'en production.
/// </para>
/// </summary>
public static class TestTokens
{
    public const string Issuer = "https://tests.cataloguemaison.local/realms/maison";
    public const string Audience = "catalogue-api";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "tests" };

    public static SecurityKey SigningKey => Key;

    /// <summary>Jeton d'un membre d'un ménage donné.</summary>
    public static string ForHousehold(string tenant, params string[] roles) =>
        Create(new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["preferred_username"] = $"test-{tenant}",
            ["tenant"] = tenant,
            ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles }
        });

    /// <summary>
    /// Jeton authentifié mais sans claim <c>tenant</c> : doit être rejeté en
    /// 401, jamais rattaché à un ménage par défaut.
    /// </summary>
    public static string WithoutHousehold(params string[] roles) =>
        Create(new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["preferred_username"] = "test-orphelin",
            ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles }
        });

    /// <summary>Jeton dont le claim <c>tenant</c> ne passe pas la validation de forme.</summary>
    public static string WithMalformedHousehold(string rawTenant, params string[] roles) =>
        Create(new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["preferred_username"] = "test-malforme",
            ["tenant"] = rawTenant,
            ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles }
        });

    private static string Create(Dictionary<string, object> claims)
    {
        var handler = new JsonWebTokenHandler();

        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
            Claims = claims
        });
    }
}
