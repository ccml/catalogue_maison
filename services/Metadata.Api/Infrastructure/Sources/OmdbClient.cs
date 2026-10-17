using System.Globalization;
using System.Text.Json;
using Metadata.Api.Domain;
using Microsoft.Extensions.Options;

namespace Metadata.Api.Infrastructure.Sources;

/// <summary>Film proposé par une recherche, avant d'en lire la fiche.</summary>
public sealed record FilmCandidate(string Id, string Title, string? Year);

/// <summary>
/// OMDb, pour les films. Ce n'est pas une source de code-barres : OMDb ne
/// cherche que par titre ou par identifiant IMDb. Il sert au chemin
/// « recherche par titre ».
/// <para>
/// La clé d'API voyage dans l'URL, OMDb n'acceptant pas d'autre forme. Elle
/// n'apparaît pas dans les journaux — la journalisation HTTP de .NET masque
/// les paramètres de requête — et ne doit jamais être recopiée dans un
/// message d'erreur.
/// </para>
/// </summary>
public sealed class OmdbClient(HttpClient http, IOptions<EnrichmentOptions> options)
{
    public const string SourceName = "omdb";

    /// <summary>Types de résultats retenus : un épisode isolé ou un jeu ne se range pas sur une étagère de films.</summary>
    private static readonly string[] KeptTypes = ["movie", "series"];

    public async Task<IReadOnlyList<FilmCandidate>> SearchAsync(string title, CancellationToken cancellationToken)
    {
        var body = await SourceHttp.GetStringAsync(
            http, SourceName, $"?apikey={Key()}&s={Uri.EscapeDataString(title)}", cancellationToken);

        return body is null ? [] : ParseSearch(body);
    }

    /// <summary>Fiche d'un film, ou <c>null</c> si OMDb ne connaît pas l'identifiant.</summary>
    public async Task<MetadataRecord?> GetAsync(FilmId id, CancellationToken cancellationToken)
    {
        var body = await SourceHttp.GetStringAsync(
            http, SourceName, $"?apikey={Key()}&i={id.Value}&plot=short", cancellationToken);

        return body is null ? null : ParseFilm(body);
    }

    private string Key() =>
        options.Value.OmdbApiKey is { Length: > 0 } key
            ? Uri.EscapeDataString(key)
            : throw new MetadataSourceException(SourceName, "aucune clé d'API configurée.");

    /// <summary>Lit une réponse de recherche (<c>s=</c>). Exposé pour les tests.</summary>
    internal static IReadOnlyList<FilmCandidate> ParseSearch(string body)
    {
        using var document = Parse(body);
        var root = document.RootElement;

        if (!IsSuccess(root))
        {
            // « Movie not found! » ou « Too many results. » : rien à proposer.
            return [];
        }

        if (!root.TryGetProperty("Search", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return results.EnumerateArray()
            .Where(r => Text(r, "Type") is { } type && KeptTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            .Select(r => (Id: Text(r, "imdbID"), Title: Text(r, "Title"), Year: Text(r, "Year")))
            .Where(r => r.Id is not null && FilmId.TryParse(r.Id, out _) && r.Title is not null)
            .Select(r => new FilmCandidate(r.Id!, r.Title!, r.Year))
            .ToList();
    }

    /// <summary>Lit une fiche (<c>i=</c>). Exposé pour les tests.</summary>
    internal static MetadataRecord? ParseFilm(string body)
    {
        using var document = Parse(body);
        var film = document.RootElement;

        if (!IsSuccess(film) || Text(film, "Title") is not { } title)
        {
            return null;
        }

        return new MetadataRecord
        {
            Kind = MetadataKind.Film,
            Title = title,
            // Le réalisateur porte la colonne commune Createur, comme
            // l'auteur d'un livre : même rôle sémantique.
            Creators = List(Text(film, "Director")),
            // « 2010 » pour un film, « 2010–2014 » pour une série : la
            // première année est celle de la sortie.
            Year = SourceHttp.ParseYear(Text(film, "Year")),
            DurationMinutes = Minutes(Text(film, "Runtime")),
            Genre = Text(film, "Genre"),
            Actors = List(Text(film, "Actors")),
            Source = SourceName
        };
    }

    private static JsonDocument Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new MetadataSourceException(SourceName, "réponse illisible.", exception);
        }
    }

    /// <summary>
    /// OMDb répond 200 même en échec, avec <c>"Response": "False"</c>. Une
    /// clé refusée ou un quota épuisé sont des pannes, pas un film inconnu.
    /// </summary>
    private static bool IsSuccess(JsonElement root)
    {
        if (Text(root, "Response") is "True")
        {
            return true;
        }

        var error = Text(root, "Error") ?? string.Empty;
        if (error.Contains("API key", StringComparison.OrdinalIgnoreCase)
            || error.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            throw new MetadataSourceException(SourceName, "clé refusée ou quota quotidien atteint.");
        }

        return false;
    }

    /// <summary>« 148 min » → 148 ; « N/A » → rien.</summary>
    private static int? Minutes(string? runtime)
    {
        var digits = new string((runtime ?? string.Empty).TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, CultureInfo.InvariantCulture, out var minutes) && minutes > 0 ? minutes : null;
    }

    /// <summary>OMDb rend ses listes en une chaîne : « Leonardo DiCaprio, Joseph Gordon-Levitt ».</summary>
    private static IReadOnlyList<string> List(string? value) =>
        value is null
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Valeur d'un champ, ou <c>null</c> — OMDb écrit « N/A » pour une valeur absente.</summary>
    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text
        && !string.Equals(text, "N/A", StringComparison.OrdinalIgnoreCase)
            ? text
            : null;
}
