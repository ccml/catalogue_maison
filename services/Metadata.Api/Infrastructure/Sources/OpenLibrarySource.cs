using System.Text.Json;
using Metadata.Api.Domain;

namespace Metadata.Api.Infrastructure.Sources;

/// <summary>
/// Open Library, en repli de la BnF : elle couvre les ouvrages étrangers que
/// le dépôt légal français ignore. Ses notices sont plus pauvres — ni série
/// ni dessinateur — et ne distinguent une BD que par ses sujets.
/// </summary>
public sealed class OpenLibrarySource(HttpClient http) : IMetadataSource
{
    public const string SourceName = "openlibrary";

    public string Name => SourceName;

    public bool Supports(ProductCode code) => code.Kind == ProductCodeKind.Isbn;

    public async Task<MetadataRecord?> LookupAsync(ProductCode code, CancellationToken cancellationToken)
    {
        var uri = $"api/books?bibkeys=ISBN:{code.Value}&format=json&jscmd=data";
        var body = await SourceHttp.GetStringAsync(http, Name, uri, cancellationToken);
        return body is null ? null : Parse(body, code);
    }

    /// <summary>Lit la réponse de l'API « books ». Exposé pour les tests.</summary>
    internal static MetadataRecord? Parse(string body, ProductCode code)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            // Un ISBN inconnu donne un objet vide, pas un 404.
            if (!document.RootElement.TryGetProperty($"ISBN:{code.Value}", out var book)
                || book.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var title = Text(book, "title");
            if (title is null)
            {
                return null;
            }

            var isComic = Names(book, "subjects").Any(subject =>
                subject.Contains("comic", StringComparison.OrdinalIgnoreCase)
                || subject.Contains("graphic novel", StringComparison.OrdinalIgnoreCase)
                || subject.Contains("bandes dessinées", StringComparison.OrdinalIgnoreCase));

            return new MetadataRecord
            {
                Kind = isComic ? MetadataKind.ComicBook : MetadataKind.Book,
                Title = Text(book, "subtitle") is { } subtitle ? $"{title}. {subtitle}" : title,
                Creators = Names(book, "authors").ToList(),
                Year = SourceHttp.ParseYear(Text(book, "publish_date")),
                Publisher = Names(book, "publishers").FirstOrDefault(),
                Pages = book.TryGetProperty("number_of_pages", out var pages)
                        && pages.ValueKind == JsonValueKind.Number
                        && pages.TryGetInt32(out var count) && count > 0
                    ? count
                    : null,
                Isbn = code.Value,
                Source = SourceName
            };
        }
        catch (JsonException exception)
        {
            throw new MetadataSourceException(SourceName, "réponse illisible.", exception);
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : null;

    /// <summary>Les listes d'Open Library sont des objets <c>{ "name": … }</c>.</summary>
    private static IEnumerable<string> Names(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && Text(item, "name") is { } name)
            {
                yield return name;
            }
        }
    }
}
