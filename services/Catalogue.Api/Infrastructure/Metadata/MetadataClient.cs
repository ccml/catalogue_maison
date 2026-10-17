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

    public async Task<MetadataLookup> LookupAsync(string code, CancellationToken cancellationToken) =>
        await ReadRecordAsync($"lookups/{Uri.EscapeDataString(code)}", code, async response =>
            (await response.Content.ReadFromJsonAsync<MetadataLookupResponse>(Json, cancellationToken))?.Record,
            cancellationToken);

    public async Task<MetadataLookup> GetFilmAsync(string imdbId, CancellationToken cancellationToken) =>
        await ReadRecordAsync($"films/{Uri.EscapeDataString(imdbId)}", imdbId, response =>
            response.Content.ReadFromJsonAsync<MetadataRecordDto>(Json, cancellationToken),
            cancellationToken);

    public async Task<bool> FilmSearchEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync("films/availability", cancellationToken);
            return response.IsSuccessStatusCode
                && (await response.Content.ReadFromJsonAsync<FilmAvailability>(Json, cancellationToken))?.Enabled == true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsUnavailability(exception))
        {
            logger.LogWarning(exception, "Disponibilité de la recherche de films inconnue.");
            return false;
        }
    }

    public async Task<FilmSearch> SearchFilmsAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"films?title={Uri.EscapeDataString(title)}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Metadata.Api a répondu {Status} à une recherche de films.", (int)response.StatusCode);
                return FilmSearch.Unavailable;
            }

            var films = await response.Content.ReadFromJsonAsync<List<FilmCandidateDto>>(Json, cancellationToken);
            return new FilmSearch(MetadataLookupOutcome.Found, films ?? []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsUnavailability(exception))
        {
            logger.LogWarning(exception, "Recherche de films indisponible.");
            return FilmSearch.Unavailable;
        }
    }

    /// <summary>
    /// Lit une notice : 200 la notice, 404 ou 400 « inconnu », tout le reste
    /// « indisponible ».
    /// </summary>
    private async Task<MetadataLookup> ReadRecordAsync(
        string uri,
        string subject,
        Func<HttpResponseMessage, Task<MetadataRecordDto?>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(uri, cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await read(response) is { } record
                        ? new MetadataLookup(MetadataLookupOutcome.Found, record)
                        : MetadataLookup.Unavailable;

                case HttpStatusCode.NotFound:
                case HttpStatusCode.BadRequest:
                    // Inconnu, ou que Metadata.Api ne sait pas interpréter :
                    // aucune source ne le connaîtra.
                    return MetadataLookup.NotFound;

                default:
                    logger.LogWarning("Metadata.Api a répondu {Status} pour {Subject}.", (int)response.StatusCode, subject);
                    return MetadataLookup.Unavailable;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsUnavailability(exception))
        {
            logger.LogWarning(exception, "Enrichissement indisponible pour {Subject}.", subject);
            return MetadataLookup.Unavailable;
        }
    }

    private static bool IsUnavailability(Exception exception) =>
        exception is HttpRequestException
            or OperationCanceledException
            or JsonException
            or ServiceTokenException;

    private sealed record FilmAvailability(bool Enabled);
}
