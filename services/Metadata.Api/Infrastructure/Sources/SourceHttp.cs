using System.Net;

namespace Metadata.Api.Infrastructure.Sources;

/// <summary>
/// Appel HTTP vers une source, avec une seule règle de traduction des
/// échecs : tout ce qui n'est pas une réponse exploitable devient une
/// <see cref="MetadataSourceException"/>. Un 404 n'en est pas une — c'est
/// la source qui dit ne pas connaître le code.
/// </summary>
internal static class SourceHttp
{
    /// <summary>Corps de la réponse, ou <c>null</c> sur un 404.</summary>
    public static async Task<string?> GetStringAsync(
        HttpClient http,
        string source,
        string requestUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(requestUri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MetadataSourceException(source, $"réponse HTTP {(int)response.StatusCode}.");
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Annulation demandée par l'appelant : ce n'est pas une panne de
            // la source, on la laisse remonter telle quelle.
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new MetadataSourceException(source, "délai de réponse dépassé.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MetadataSourceException(source, "source injoignable.", exception);
        }
    }

    /// <summary>Premier nombre de quatre chiffres plausible comme année, ou <c>null</c>.</summary>
    public static int? ParseYear(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        for (var i = 0; i + 4 <= raw.Length; i++)
        {
            if (raw.AsSpan(i, 4) is var span
                && char.IsAsciiDigit(span[0]) && char.IsAsciiDigit(span[1])
                && char.IsAsciiDigit(span[2]) && char.IsAsciiDigit(span[3])
                && (i + 4 == raw.Length || !char.IsAsciiDigit(raw[i + 4]))
                && (i == 0 || !char.IsAsciiDigit(raw[i - 1]))
                && int.TryParse(span, out var year)
                && year is >= 1400 and <= 2200)
            {
                return year;
            }
        }

        return null;
    }
}
