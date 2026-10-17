using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Catalogue.Api.Infrastructure.Metadata;

/// <summary>
/// Client de Metadata.Api. Toute défaillance — service arrêté, jeton
/// refusé, délai dépassé, réponse illisible — devient
/// <see cref="MetadataLookupOutcome.Unavailable"/> : l'échec de
/// l'enrichissement n'empêche jamais l'ajout d'un média.
/// </summary>
public sealed class MetadataClient(HttpClient http, ILogger<MetadataClient> logger) : IMetadataLookup
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<MetadataLookup> LookupAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"lookups/{Uri.EscapeDataString(code)}", cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var body = await response.Content.ReadFromJsonAsync<MetadataLookupResponse>(Json, cancellationToken);
                    return body?.Record is { } record
                        ? new MetadataLookup(MetadataLookupOutcome.Found, record)
                        : MetadataLookup.Unavailable;

                case HttpStatusCode.NotFound:
                    return MetadataLookup.NotFound;

                case HttpStatusCode.BadRequest:
                    // Code que Metadata.Api ne sait pas interpréter : aucune
                    // source ne le connaîtra.
                    return MetadataLookup.NotFound;

                default:
                    logger.LogWarning("Metadata.Api a répondu {Status} pour {Code}.", (int)response.StatusCode, code);
                    return MetadataLookup.Unavailable;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or OperationCanceledException
                                              or JsonException
                                              or ServiceTokenException)
        {
            logger.LogWarning(exception, "Enrichissement indisponible pour {Code}.", code);
            return MetadataLookup.Unavailable;
        }
    }
}
