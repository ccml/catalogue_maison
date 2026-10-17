using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Catalogue.Api.Infrastructure.Metadata;

/// <summary>
/// Jeton de service de Catalogue.Api, obtenu auprès de Keycloak en client
/// credentials et réutilisé jusqu'à peu avant son expiration : Keycloak
/// n'est sollicité qu'au renouvellement, pas à chaque scan.
/// <para>
/// Singleton, pour que ce cache vive aussi longtemps que le processus ; le
/// client HTTP est donc pris à la fabrique à chaque renouvellement.
/// </para>
/// </summary>
public sealed class ServiceTokenProvider(
    IHttpClientFactory httpClients,
    IOptions<KeycloakOptions> keycloak,
    IOptions<MetadataOptions> metadata,
    TimeProvider clock)
{
    /// <summary>Marge avant expiration : un jeton qui expire en vol serait refusé.</summary>
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromSeconds(30);

    public const string HttpClientName = "keycloak-token";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _renewAt;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is { } current && clock.GetUtcNow() < _renewAt)
        {
            return current;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is { } refreshed && clock.GetUtcNow() < _renewAt)
            {
                return refreshed;
            }

            var options = metadata.Value;
            if (string.IsNullOrEmpty(options.ClientSecret))
            {
                throw new ServiceTokenException("« Metadata:ClientSecret » n'est pas configuré.");
            }

            var endpoint = $"{keycloak.Value.Authority.TrimEnd('/')}/protocol/openid-connect/token";
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials"
                })
            };

            // Authentification du client par HTTP Basic, comme le prévoit
            // OAuth2 : le secret ne transite pas dans le corps.
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                    $"{Uri.EscapeDataString(options.ClientId)}:{Uri.EscapeDataString(options.ClientSecret)}")));

            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ServiceTokenException($"Keycloak a refusé le jeton de service ({(int)response.StatusCode}).");
            }

            var body = await JsonSerializer.DeserializeAsync<TokenResponse>(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            if (body?.AccessToken is not { Length: > 0 } token)
            {
                throw new ServiceTokenException("Réponse de Keycloak sans jeton d'accès.");
            }

            _token = token;
            _renewAt = clock.GetUtcNow() + TimeSpan.FromSeconds(Math.Max(body.ExpiresIn, 0)) - RenewalMargin;
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Le jeton de service n'a pas pu être obtenu.</summary>
public sealed class ServiceTokenException(string message) : Exception(message);

/// <summary>Pose le jeton de service sur chaque appel à Metadata.Api.</summary>
public sealed class ServiceTokenHandler(ServiceTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await tokens.GetTokenAsync(cancellationToken));

        return await base.SendAsync(request, cancellationToken);
    }
}
