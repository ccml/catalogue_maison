using System.Text.Json;
using Metadata.Api.Domain;

namespace Metadata.Api.Infrastructure.Sources;

/// <summary>
/// MusicBrainz, pour les codes-barres de disques.
/// <para>
/// Deux appels : une recherche par code-barres, qui ne renvoie pas les
/// pistes, puis la lecture de l'édition retenue. MusicBrainz limite chaque
/// client à une requête par seconde ; <see cref="MusicBrainzRateGate"/> y
/// veille pour tout le processus.
/// </para>
/// </summary>
public sealed class MusicBrainzSource(HttpClient http, MusicBrainzRateGate gate) : IMetadataSource
{
    public const string SourceName = "musicbrainz";

    /// <summary>Supports physiques, préférés aux éditions numériques qui partagent souvent le même code.</summary>
    private static readonly string[] PhysicalFormats = ["CD", "Vinyl", "Cassette", "SACD"];

    public string Name => SourceName;

    /// <summary>
    /// Tout code qui n'est pas un ISBN : un film ou un jeu n'y sera pas
    /// trouvé, et c'est sans conséquence — un code inconnu est mis en cache
    /// comme tel.
    /// </summary>
    public bool Supports(ProductCode code) => code.Kind == ProductCodeKind.Ean;

    public async Task<MetadataRecord?> LookupAsync(ProductCode code, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(string.Join(" OR ", BarcodeVariants(code.Value).Select(b => $"barcode:{b}")));

        await gate.WaitAsync(cancellationToken);
        var search = await SourceHttp.GetStringAsync(http, Name, $"release/?query={query}&limit=10&fmt=json", cancellationToken);
        var releaseId = search is null ? null : PickRelease(search, code.Value);
        if (releaseId is null)
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken);
        var release = await SourceHttp.GetStringAsync(
            http, Name, $"release/{releaseId}?inc=recordings+artist-credits+labels&fmt=json", cancellationToken);

        return release is null ? null : ParseRelease(release);
    }

    /// <summary>
    /// Un UPC-A de 12 chiffres et son EAN-13 préfixé d'un zéro désignent le
    /// même disque, et MusicBrainz stocke l'un ou l'autre selon qui a saisi.
    /// </summary>
    internal static IEnumerable<string> BarcodeVariants(string code)
    {
        yield return code;

        if (code.Length == 12)
        {
            yield return "0" + code;
        }
        else if (code.Length == 13 && code[0] == '0')
        {
            yield return code[1..];
        }
    }

    /// <summary>
    /// Choisit l'édition dont le code-barres correspond exactement, en
    /// préférant un support physique. Exposé pour les tests.
    /// </summary>
    internal static string? PickRelease(string body, string code)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("releases", out var releases)
                || releases.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var variants = BarcodeVariants(code).ToHashSet(StringComparer.Ordinal);

            var candidates = releases.EnumerateArray()
                .Where(r => Text(r, "barcode") is { } barcode && variants.Contains(barcode))
                .Select(r => (Id: Text(r, "id"), Physical: Formats(r).Any(IsPhysical)))
                .Where(r => r.Id is not null)
                .ToList();

            return candidates.FirstOrDefault(c => c.Physical).Id ?? candidates.FirstOrDefault().Id;
        }
        catch (JsonException exception)
        {
            throw new MetadataSourceException(SourceName, "réponse de recherche illisible.", exception);
        }
    }

    /// <summary>Lit une édition complète. Exposé pour les tests.</summary>
    internal static MetadataRecord? ParseRelease(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var release = document.RootElement;

            var title = Text(release, "title");
            if (title is null)
            {
                return null;
            }

            List<string> artists = [];
            if (release.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
            {
                artists.AddRange(credits.EnumerateArray()
                    .Select(c => Text(c, "name"))
                    .OfType<string>());
            }

            string? label = null;
            if (release.TryGetProperty("label-info", out var labels) && labels.ValueKind == JsonValueKind.Array)
            {
                label = labels.EnumerateArray()
                    .Select(l => l.TryGetProperty("label", out var inner) && inner.ValueKind == JsonValueKind.Object
                        ? Text(inner, "name")
                        : null)
                    .FirstOrDefault(name => name is not null);
            }

            List<string> tracks = [];
            if (release.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array)
            {
                foreach (var medium in media.EnumerateArray())
                {
                    if (medium.TryGetProperty("tracks", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        tracks.AddRange(items.EnumerateArray().Select(t => Text(t, "title")).OfType<string>());
                    }
                }
            }

            return new MetadataRecord
            {
                Kind = MetadataKind.Music,
                Title = title,
                Creators = artists.Distinct(StringComparer.Ordinal).ToList(),
                Year = SourceHttp.ParseYear(Text(release, "date")),
                Label = label,
                Format = Formats(release).FirstOrDefault(),
                Tracks = tracks,
                Source = SourceName
            };
        }
        catch (JsonException exception)
        {
            throw new MetadataSourceException(SourceName, "édition illisible.", exception);
        }
    }

    private static bool IsPhysical(string format) =>
        PhysicalFormats.Any(physical => format.Contains(physical, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Formats(JsonElement release)
    {
        if (!release.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return media.EnumerateArray().Select(m => Text(m, "format")).OfType<string>().ToList();
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : null;
}

/// <summary>
/// Espace les appels à MusicBrainz d'au moins une seconde, pour tout le
/// processus. Dépasser la limite vaut un 503 de leur part, puis un blocage.
/// </summary>
public sealed class MusicBrainzRateGate(TimeProvider clock)
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1100);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTimeOffset _next = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var delay = _next - clock.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, clock, cancellationToken);
            }

            _next = clock.GetUtcNow() + Interval;
        }
        finally
        {
            _lock.Release();
        }
    }
}
