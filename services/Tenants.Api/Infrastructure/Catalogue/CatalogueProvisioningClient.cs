using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Infrastructure.Catalogue;

public sealed class CatalogueOptions
{
    public const string Section = "Catalogue";

    /// <summary>Racine de Catalogue.Api, par ex. <c>http://localhost:5080/</c>.</summary>
    public Uri? BaseUrl { get; init; }

    public int TimeoutSeconds { get; init; } = 60;
}

/// <summary>
/// Demande à Catalogue.Api de créer le schéma d'un ménage. Tenants.Api ne
/// touche jamais à la base <c>catalogue</c> : il passe par le contrat HTTP
/// du service qui la possède.
/// </summary>
public interface ICatalogueProvisioning
{
    /// <summary>Idempotent : rejoué sur un schéma déjà à jour, ne fait rien.</summary>
    /// <exception cref="CatalogueUnavailableException">Si Catalogue.Api n'a pas confirmé.</exception>
    Task ProvisionSchemaAsync(TenantId household, CancellationToken cancellationToken);

    /// <summary>Supprime le schéma et tout le catalogue du ménage. Irréversible, idempotent.</summary>
    /// <exception cref="CatalogueUnavailableException">Si Catalogue.Api n'a pas confirmé.</exception>
    Task DropSchemaAsync(TenantId household, CancellationToken cancellationToken);
}

/// <summary>
/// Appel en client credentials, avec le jeton de service de Tenants.Api — le
/// seul à porter le rôle <c>provisionnement</c>. Le jeton de l'opérateur
/// n'est jamais relayé.
/// </summary>
public sealed class CatalogueProvisioningClient(HttpClient http, ILogger<CatalogueProvisioningClient> logger)
    : ICatalogueProvisioning
{
    public Task ProvisionSchemaAsync(TenantId household, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, household, cancellationToken);

    public Task DropSchemaAsync(TenantId household, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, household, cancellationToken);

    private async Task SendAsync(HttpMethod method, TenantId household, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(method, $"admin/households/{household.Value}/schema");
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Catalogue.Api injoignable ({Method} du schéma du ménage {Household}).", method, household.Value);
            throw new CatalogueUnavailableException();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Catalogue.Api a refusé le {Method} du schéma du ménage {Household} : {Status}.",
                    method, household.Value, (int)response.StatusCode);
                throw new CatalogueUnavailableException();
            }
        }
    }
}

/// <summary>Catalogue.Api n'a pas confirmé : l'opération en cours est à rejouer.</summary>
public sealed class CatalogueUnavailableException()
    : Exception("Le catalogue du ménage n'a pas répondu : relancez l'opération, elle reprendra où elle s'est arrêtée.");
