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

    /// <summary>Indice des bandes dessinées dans le cadre de classement de la Bibliographie nationale française.</summary>
    private const string ComicsClassification = "805";

    /// <summary>Thésaurus des formes audiovisuelles : décrit un support (livre lu), pas un genre.</summary>
    private const string AudiovisualThesaurus = "frTAV";

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

        // Une notice provisoire, établie d'après les données de l'éditeur
        // avant catalogage, porte « n » (description non ISBD) en position 18
        // du label ; une notice cataloguée y porte un blanc.
        var leader = record.Element(Marc + "leader")?.Value ?? string.Empty;
        var provisional = leader.Length > 18 && leader[18] == 'n';

        var fields = record.Elements(Marc + "datafield")
            .Select(field => new Field(
                (string?)field.Attribute("tag") ?? string.Empty,
                (string?)field.Attribute("ind2") ?? string.Empty,
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

        var genres = fields
            .Where(f => f.Tag == "608" && f.First('2') != AudiovisualThesaurus)
            .SelectMany(f => f.All('a'))
            .ToList();

        var isComic = IsComic(
            genres,
            fields.FirstOrDefault(f => f.Tag == "105")?.First('a'),
            fields.Where(f => f.Tag == "686").Select(f => f.First('a')));

        // L'adresse de publication est en 210 dans les notices anciennes, en
        // 214 dans les récentes. Une notice récente peut porter plusieurs 214
        // — l'imprimeur a le sien (second indicateur 3) : seul celui de la
        // publication (0) désigne l'éditeur.
        var publication = fields.FirstOrDefault(f => f.Tag == "214" && f.Indicator2 == "0")
            ?? fields.FirstOrDefault(f => f.Tag == "210");
        var publisher = publication?.First('c');
        var year = SourceHttp.ParseYear(publication?.First('d'));

        var pages = PagesPattern().Match(fields.FirstOrDefault(f => f.Tag == "215")?.First('a') ?? string.Empty) is
            { Success: true } match
                ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : (int?)null;

        // L'UNIMARC distingue ce que la couverture confond : 461 est la
        // série de l'œuvre (Harry Potter, 3 ; Blacksad, 3), 410 la
        // collection de l'éditeur (Folio policier, 85). La zone 225 n'est que
        // leur transcription telle qu'imprimée, l'une ou l'autre : elle n'est
        // pas lue, pour ne jamais prendre une collection pour une série.
        var seriesLink = fields.FirstOrDefault(f => f.Tag == "461");
        var collectionLink = fields.FirstOrDefault(f => f.Tag == "410");
        var partTitle = titleField!.First('i');

        var series = Punctuation(seriesLink?.First('t'));
        var volume = ParseVolume(seriesLink?.First('v'));
        var title = mainTitle;

        if (isComic)
        {
            // Sans lien 461, la zone de titre d'un album porte la série en $a,
            // le numéro en $h et le titre de l'album en $i.
            series ??= partTitle is not null ? mainTitle : null;
            volume ??= ParseVolume(titleField.First('h'));
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
            // Lue telle quelle : son point final est celui d'une abréviation
            // (« Nouvelle éd. »), pas une ponctuation ISBD à retirer.
            Edition = fields.FirstOrDefault(f => f.Tag == "205")?.First('a'),
            Collection = Punctuation(collectionLink?.First('t')),
            CollectionNumber = Punctuation(collectionLink?.First('v')),
            Genre = isComic ? null : GenreOf(genres, titleField.First('e')),
            Illustrator = isComic ? illustrator : null,
            Provisional = provisional,
            Source = SourceName
        };
    }

    /// <summary>
    /// Une BD se reconnaît à l'un de trois signaux, dont aucun ne suffit
    /// seul — relevés sur une vingtaine de notices réelles :
    /// <list type="bullet">
    ///   <item>
    ///     la forme du contenu, positions 4 à 7 des données codées (105 $a),
    ///     qui vaut <c>t</c> pour une bande dessinée. Le plus fiable : présent
    ///     sur presque toutes les BD, absent des romans, albums jeunesse et
    ///     livres illustrés ;
    ///   </item>
    ///   <item>
    ///     le genre « Bandes dessinées » ou « Mangas » (608), mais seulement
    ///     dans les notices relues par le CNLJ, donc surtout en jeunesse ;
    ///   </item>
    ///   <item>
    ///     l'indice 805 du cadre de classement de la Bibliographie nationale
    ///     française (686) : jamais vu hors BD, mais absent de la plupart.
    ///   </item>
    /// </list>
    /// La présence d'un dessinateur, elle, n'est pas un signal : un album
    /// jeunesse ou un livre illustré en ont un.
    /// </summary>
    internal static bool IsComic(IEnumerable<string> formGenres, string? codedData, IEnumerable<string?> classification)
    {
        var contentForm = codedData is { Length: >= 8 } ? codedData[4..8] : string.Empty;

        return contentForm.Contains('t', StringComparison.Ordinal)
            || formGenres.Any(genre =>
                genre.Contains("bandes dessinées", StringComparison.OrdinalIgnoreCase)
                || genre.Contains("mangas", StringComparison.OrdinalIgnoreCase))
            || classification.Any(code => code?.Trim() == ComicsClassification);
    }

    /// <summary>
    /// Genre d'un livre. La BnF ne le donne franchement que pour la
    /// littérature jeunesse (zone 608, avis du CNLJ, au pluriel : « Romans »).
    /// À défaut, le complément du titre en porte souvent un (« roman »), mais
    /// il peut aussi bien porter un sous-titre : il n'est retenu que s'il est
    /// un genre connu. Sinon, rien — l'utilisateur complète.
    /// </summary>
    internal static string? GenreOf(IEnumerable<string> formGenres, string? titleComplement)
    {
        foreach (var genre in formGenres)
        {
            if (KnownGenres.TryGetValue(Punctuation(genre) ?? string.Empty, out var known))
            {
                return known;
            }
        }

        return KnownGenres.TryGetValue(Punctuation(titleComplement) ?? string.Empty, out var fromTitle)
            ? fromTitle
            : null;
    }

    /// <summary>
    /// Formes rencontrées dans les notices, ramenées à un libellé unique : un
    /// genre sert à regrouper, il ne doit pas exister en trois variantes.
    /// </summary>
    private static readonly Dictionary<string, string> KnownGenres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["roman"] = "Roman",
        ["romans"] = "Roman",
        ["roman policier"] = "Policier",
        ["romans policiers"] = "Policier",
        ["policier"] = "Policier",
        ["thriller"] = "Thriller",
        ["science-fiction"] = "Science-fiction",
        ["fantasy"] = "Fantasy",
        ["nouvelles"] = "Nouvelles",
        ["nouvelle"] = "Nouvelles",
        ["récit"] = "Récit",
        ["récits"] = "Récit",
        ["conte"] = "Conte",
        ["contes"] = "Conte",
        ["poésie"] = "Poésie",
        ["poèmes"] = "Poésie",
        ["théâtre"] = "Théâtre",
        ["essai"] = "Essai",
        ["essais"] = "Essai",
        ["biographie"] = "Biographie",
        ["biographies"] = "Biographie",
        ["documentaire"] = "Documentaire",
        ["documentaires"] = "Documentaire",
        ["album"] = "Album",
        ["albums"] = "Album"
    };

    /// <summary>
    /// Retire la ponctuation ISBD de fin (« Harry Potter. ») qui sépare les
    /// zones dans une notice imprimée et n'a rien à faire dans une valeur.
    /// </summary>
    private static string? Punctuation(string? value)
    {
        var trimmed = value?.TrimEnd(' ', '.', ',', ';', ':', '/', '=').Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
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

    private sealed record Field(string Tag, string Indicator2, List<(string Code, string Value)> Subfields)
    {
        public string? First(char code) =>
            Subfields.FirstOrDefault(s => s.Code.Length == 1 && s.Code[0] == code && s.Value.Length > 0).Value;

        public IEnumerable<string> All(char code) =>
            Subfields.Where(s => s.Code.Length == 1 && s.Code[0] == code && s.Value.Length > 0).Select(s => s.Value);
    }
}
