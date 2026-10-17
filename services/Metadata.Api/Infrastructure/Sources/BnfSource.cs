using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Metadata.Api.Domain;

namespace Metadata.Api.Infrastructure.Sources;

/// <summary>
/// Catalogue général de la BnF, via son interface SRU, en UNIMARC.
/// <para>
/// Source principale des livres et BD : le dépôt légal y fait figurer tout
/// ce qui est publié en France, et l'UNIMARC distingue ce que le Dublin Core
/// mélange — la série et le tome d'un album (461), le scénariste et le
/// dessinateur par leur code de rôle (070 et 440).
/// </para>
/// <para>
/// La BnF n'indexe pas les ISBN sous une forme unique : les notices
/// anciennes portent l'ISBN-10, les récentes l'ISBN-13, et l'EAN est un
/// index à part. La requête interroge les trois.
/// </para>
/// </summary>
public sealed partial class BnfSource(HttpClient http) : IMetadataSource
{
    public const string SourceName = "bnf";

    private const string AuthorRole = "070";
    private const string IllustratorRole = "440";

    private static readonly XNamespace Marc = "info:lc/xmlns/marcxchange-v2";
    private static readonly XNamespace Srw = "http://www.loc.gov/zing/srw/";

    public string Name => SourceName;

    public bool Supports(ProductCode code) => code.Kind == ProductCodeKind.Isbn;

    public async Task<MetadataRecord?> LookupAsync(ProductCode code, CancellationToken cancellationToken)
    {
        var clauses = new List<string>
        {
            $"bib.ean adj \"{code.Value}\"",
            $"bib.isbn adj \"{code.Value}\""
        };
        if (code.Isbn10 is { } isbn10)
        {
            clauses.Add($"bib.isbn adj \"{isbn10}\"");
        }

        var query = Uri.EscapeDataString(string.Join(" or ", clauses));
        var uri = $"SRU?version=1.2&operation=searchRetrieve&recordSchema=unimarcxchange&maximumRecords=1&query={query}";

        var body = await SourceHttp.GetStringAsync(http, Name, uri, cancellationToken);
        return body is null ? null : Parse(body, code);
    }

    /// <summary>Lit la première notice d'une réponse SRU. Exposé pour les tests.</summary>
    internal static MetadataRecord? Parse(string body, ProductCode code)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(body);
        }
        catch (XmlException exception)
        {
            throw new MetadataSourceException(SourceName, "réponse illisible.", exception);
        }

        if (document.Root?.Name != Srw + "searchRetrieveResponse")
        {
            throw new MetadataSourceException(SourceName, "réponse SRU inattendue.");
        }

        var record = document.Descendants(Marc + "record").FirstOrDefault();
        if (record is null)
        {
            return null;
        }

        var fields = record.Elements(Marc + "datafield")
            .Select(field => new Field(
                (string?)field.Attribute("tag") ?? string.Empty,
                field.Elements(Marc + "subfield")
                    .Select(sub => ((string?)sub.Attribute("code") ?? string.Empty, Clean(sub.Value)))
                    .ToList()))
            .ToList();

        var titleField = fields.FirstOrDefault(f => f.Tag == "200");
        var mainTitle = titleField?.First('a');
        if (string.IsNullOrEmpty(mainTitle))
        {
            return null;
        }

        var people = fields
            .Where(f => f.Tag is "700" or "701" or "702")
            .Select(f => (Name: PersonName(f), Roles: f.All('4').ToHashSet(StringComparer.Ordinal)))
            .Where(p => p.Name is not null)
            .ToList();

        var creators = people
            .Where(p => p.Roles.Count == 0 || p.Roles.Contains(AuthorRole))
            .Select(p => p.Name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var illustrator = people.FirstOrDefault(p => p.Roles.Contains(IllustratorRole)).Name;

        var isComic = fields
            .Where(f => f.Tag == "608")
            .SelectMany(f => f.All('a'))
            .Any(genre => genre.Contains("bandes dessinées", StringComparison.OrdinalIgnoreCase)
                       || genre.Contains("mangas", StringComparison.OrdinalIgnoreCase));

        // L'adresse de publication est en 210 dans les notices anciennes, en
        // 214 dans les récentes.
        var publication = fields.FirstOrDefault(f => f.Tag is "214" or "210");
        var publisher = publication?.First('c');
        var year = SourceHttp.ParseYear(publication?.First('d'));

        var pages = PagesPattern().Match(fields.FirstOrDefault(f => f.Tag == "215")?.First('a') ?? string.Empty) is
            { Success: true } match
                ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : (int?)null;

        // Une BD se range par série et tome. Le lien 461 les donne
        // proprement ; à défaut, la zone de titre porte la série en $a, le
        // numéro en $h et le titre de l'album en $i.
        var seriesLink = fields.FirstOrDefault(f => f.Tag == "461");
        var partTitle = titleField!.First('i');
        string? series = null;
        int? volume = null;
        var title = mainTitle;

        if (isComic)
        {
            series = seriesLink?.First('t') ?? (partTitle is not null ? mainTitle : null);
            volume = ParseVolume(seriesLink?.First('v') ?? titleField.First('h'));
            title = partTitle ?? mainTitle;
        }
        else if (partTitle is not null)
        {
            title = $"{mainTitle}. {partTitle}";
        }

        return new MetadataRecord
        {
            Kind = isComic ? MetadataKind.ComicBook : MetadataKind.Book,
            Title = title,
            Creators = creators,
            Year = year,
            Publisher = publisher,
            Pages = pages,
            Isbn = code.Value,
            Series = series,
            Volume = volume,
            Illustrator = isComic ? illustrator : null,
            Source = SourceName
        };
    }

    private static string? PersonName(Field field)
    {
        var surname = field.First('a');
        var forename = field.First('b');
        return (surname, forename) switch
        {
            (null, _) => null,
            (_, null) => surname,
            _ => $"{forename} {surname}"
        };
    }

    private static int? ParseVolume(string? raw) =>
        raw is not null && VolumePattern().Match(raw) is { Success: true } match
            && int.TryParse(match.Value, CultureInfo.InvariantCulture, out var volume) && volume > 0
            ? volume
            : null;

    /// <summary>
    /// Retire les caractères de non-tri de l'UNIMARC, qui encadrent les
    /// articles ignorés au classement (« Le », « La »…) et n'ont rien à
    /// faire dans un titre affiché.
    /// </summary>
    private static string Clean(string value) =>
        value.Replace("\u0088", string.Empty, StringComparison.Ordinal)
             .Replace("\u0089", string.Empty, StringComparison.Ordinal)
             .Replace("\u0098", string.Empty, StringComparison.Ordinal)
             .Replace("\u009C", string.Empty, StringComparison.Ordinal)
             .Trim();

    [GeneratedRegex(@"(\d+)\s*p\.", RegexOptions.CultureInvariant)]
    private static partial Regex PagesPattern();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex VolumePattern();

    private sealed record Field(string Tag, List<(string Code, string Value)> Subfields)
    {
        public string? First(char code) =>
            Subfields.FirstOrDefault(s => s.Code.Length == 1 && s.Code[0] == code && s.Value.Length > 0).Value;

        public IEnumerable<string> All(char code) =>
            Subfields.Where(s => s.Code.Length == 1 && s.Code[0] == code && s.Value.Length > 0).Select(s => s.Value);
    }
}
