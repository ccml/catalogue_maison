using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tenants.Api.Domain;
using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Infrastructure.Keycloak;

/// <summary>
/// Annuaire adossé à l'Admin REST API de Keycloak, sous le jeton du compte
/// de service <c>tenants-api</c>.
/// </summary>
public sealed class KeycloakIdentityDirectory(HttpClient http) : IIdentityDirectory
{
    public const string TenantAttribute = "tenant";

    /// <summary>Bien au-delà de la taille d'un foyer ; au-delà, on refuse plutôt que de tronquer.</summary>
    private const int MaxAccountsPerTenant = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DirectoryAccount>> ListByTenantAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var query = $"users?briefRepresentation=false&max={MaxAccountsPerTenant + 1}"
            + $"&q={Uri.EscapeDataString($"{TenantAttribute}:{tenant.Value}")}";

        var users = await GetAsync<List<UserRepresentation>>(query, cancellationToken) ?? [];
        if (users.Count > MaxAccountsPerTenant)
        {
            throw new IdentityDirectoryException($"Plus de {MaxAccountsPerTenant} comptes pour un ménage.");
        }

        // La recherche par attribut de Keycloak fait foi, mais on ne lui
        // délègue pas la frontière entre ménages : chaque compte est revérifié.
        return users
            .Select(ToAccount)
            .Where(account => string.Equals(account.Tenant, tenant.Value, StringComparison.Ordinal))
            .ToList();
    }

    public async Task<DirectoryAccount?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetAsync<UserRepresentation>($"users/{id}", cancellationToken, allowNotFound: true);
        return user is null ? null : ToAccount(user);
    }

    public async Task<DirectoryAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var users = await GetAsync<List<UserRepresentation>>(
            $"users?exact=true&briefRepresentation=false&username={Uri.EscapeDataString(username)}",
            cancellationToken) ?? [];

        return users
            .Where(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))
            .Select(ToAccount)
            .FirstOrDefault();
    }

    public async Task<Guid> CreateAsync(NewAccount account, CancellationToken cancellationToken)
    {
        var body = new
        {
            username = account.Username,
            firstName = account.FirstName,
            lastName = account.LastName,
            enabled = true,
            attributes = new Dictionary<string, string[]> { [TenantAttribute] = [account.Tenant.Value] },
            credentials = new[] { new { type = "password", value = account.TemporaryPassword, temporary = true } }
        };

        using var response = await SendAsync(HttpMethod.Post, "users", body, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new UsernameTakenException(account.Username);
        }

        await ThrowIfPasswordRejectedAsync(response, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        // Keycloak ne renvoie pas le compte créé, seulement son adresse.
        var location = response.Headers.Location?.ToString() ?? string.Empty;
        return Guid.TryParse(location.TrimEnd('/').Split('/')[^1], out var id)
            ? id
            : throw new IdentityDirectoryException("Keycloak n'a pas indiqué l'identifiant du compte créé.");
    }

    public async Task<IReadOnlySet<string>> GetRealmRolesAsync(Guid id, CancellationToken cancellationToken)
    {
        var roles = await GetAsync<List<RoleRepresentation>>($"users/{id}/role-mappings/realm", cancellationToken) ?? [];
        return roles.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
    }

    public async Task SetManagedRolesAsync(Guid id, IReadOnlySet<string> wanted, CancellationToken cancellationToken)
    {
        if (wanted.Except(Roles.Managed).FirstOrDefault() is { } foreign)
        {
            throw new ArgumentException($"Rôle « {foreign} » hors du périmètre de ce service.", nameof(wanted));
        }

        var current = await GetAsync<List<RoleRepresentation>>($"users/{id}/role-mappings/realm", cancellationToken) ?? [];

        var toRemove = current
            .Where(r => Roles.Managed.Contains(r.Name) && !wanted.Contains(r.Name))
            .ToList();

        var missing = wanted.Except(current.Select(r => r.Name)).ToHashSet(StringComparer.Ordinal);
        List<RoleRepresentation> toAdd = [];
        if (missing.Count > 0)
        {
            // Les rôles s'attribuent par leur représentation complète : on la
            // prend dans ceux que le compte peut recevoir.
            var available = await GetAsync<List<RoleRepresentation>>(
                $"users/{id}/role-mappings/realm/available", cancellationToken) ?? [];
            toAdd = available.Where(r => missing.Contains(r.Name)).ToList();

            if (toAdd.Count != missing.Count)
            {
                throw new IdentityDirectoryException("Un rôle de ménage est absent du realm.");
            }
        }

        if (toAdd.Count > 0)
        {
            using var added = await SendAsync(HttpMethod.Post, $"users/{id}/role-mappings/realm", toAdd, cancellationToken);
            await EnsureSuccessAsync(added, cancellationToken);
        }

        if (toRemove.Count > 0)
        {
            using var removed = await SendAsync(HttpMethod.Delete, $"users/{id}/role-mappings/realm", toRemove, cancellationToken);
            await EnsureSuccessAsync(removed, cancellationToken);
        }
    }

    public async Task SetTemporaryPasswordAsync(Guid id, string password, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Put,
            $"users/{id}/reset-password",
            new { type = "password", value = password, temporary = true },
            cancellationToken);

        await ThrowIfPasswordRejectedAsync(response, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        // Représentation partielle : Keycloak ne touche qu'aux champs
        // présents, l'attribut tenant est préservé.
        using var response = await SendAsync(HttpMethod.Put, $"users/{id}", new { enabled }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SignOutEverywhereAsync(Guid id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"users/{id}/logout", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static DirectoryAccount ToAccount(UserRepresentation user) => new(
        user.Id,
        user.Username,
        user.FirstName,
        user.LastName,
        user.Enabled,
        user.Attributes?.GetValueOrDefault(TenantAttribute) is [var tenant] ? tenant : null);

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(path, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new IdentityDirectoryException($"Keycloak injoignable : {exception.Message}");
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            await EnsureSuccessAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new IdentityDirectoryException($"Keycloak injoignable : {exception.Message}");
        }
    }

    /// <summary>
    /// Keycloak répond 400 avec un <c>error_description</c> lisible quand la
    /// politique de mot de passe du realm refuse la valeur.
    /// </summary>
    private static async Task ThrowIfPasswordRejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            return;
        }

        var error = await TryReadErrorAsync(response, cancellationToken);
        if (error?.Error?.StartsWith("invalidPassword", StringComparison.Ordinal) == true)
        {
            throw new PasswordRejectedException(error.ErrorDescription);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await TryReadErrorAsync(response, cancellationToken);
        var detail = error?.ErrorDescription ?? error?.ErrorMessage ?? error?.Error;
        throw new IdentityDirectoryException(
            $"Keycloak a répondu {(int)response.StatusCode} sur {response.RequestMessage?.Method} "
            + $"{response.RequestMessage?.RequestUri?.AbsolutePath}{(detail is null ? "" : $" : {detail}")}.");
    }

    private static async Task<ErrorRepresentation?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorRepresentation>(Json, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record UserRepresentation(
        Guid Id,
        string Username,
        string? FirstName,
        string? LastName,
        bool Enabled,
        Dictionary<string, string[]>? Attributes);

    private sealed record RoleRepresentation(string Id, string Name);

    private sealed record ErrorRepresentation(
        [property: System.Text.Json.Serialization.JsonPropertyName("error")] string? Error,
        [property: System.Text.Json.Serialization.JsonPropertyName("error_description")] string? ErrorDescription,
        [property: System.Text.Json.Serialization.JsonPropertyName("errorMessage")] string? ErrorMessage);
}
