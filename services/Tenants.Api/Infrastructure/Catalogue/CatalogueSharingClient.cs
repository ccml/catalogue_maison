using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Infrastructure.Catalogue;

/// <summary>Réponse de Catalogue.Api, relayée telle quelle au client.</summary>
public sealed record CatalogueReply(int StatusCode, string? ContentType, byte[] Body);

/// <summary>
/// Lit le catalogue d'un ménage pour un ménage partenaire, et y dépose ses
/// demandes de prêt, par les endpoints internes de partage de Catalogue.Api.
/// <para>
/// L'appelant a déjà vérifié le partage : ce client ne fait que porter le
/// ménage prêteur et le partage jusqu'à Catalogue.Api. Les contrats sont ceux
/// de Catalogue.Api, rendus tels quels — ils sont déjà restreints à ce qu'un
/// ménage partenaire peut voir, et les redéfinir ici serait une seconde
/// occasion de divergence.
/// </para>
/// </summary>
public interface ICatalogueSharing
{
    /// <exception cref="CatalogueUnavailableException">Si Catalogue.Api est injoignable.</exception>
    Task<CatalogueReply> SendAsync(
        HttpMethod method,
        TenantId owner,
        Guid shareId,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        object? body,
        CancellationToken cancellationToken);
}

/// <summary>
/// Appel en client credentials, avec le jeton de service de Tenants.Api — le
/// seul à porter le rôle <c>partage</c>. Le jeton de l'utilisateur n'est
/// jamais relayé.
/// </summary>
public sealed class CatalogueSharingClient(HttpClient http, ILogger<CatalogueSharingClient> logger) : ICatalogueSharing
{
    public async Task<CatalogueReply> SendAsync(
        HttpMethod method,
        TenantId owner,
        Guid shareId,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        object? body,
        CancellationToken cancellationToken)
    {
        var uri = $"internal/households/{owner.Value}/shares/{shareId}/{path}";
        if (query is not null)
        {
            uri = QueryHelpers.AddQueryString(uri, query.Where(p => !string.IsNullOrEmpty(p.Value)));
        }

        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await http.SendAsync(request, cancellationToken);

            return new CatalogueReply(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                await response.Content.ReadAsByteArrayAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Catalogue.Api injoignable ({Method} {Path} pour un partage).", method, path);
            throw new CatalogueUnavailableException();
        }
    }
}
