using Catalogue.Api.Domain;

namespace Catalogue.Api.Features.Transfer;

/// <summary>
/// Reconnaît, à l'import, une fiche déjà présente au catalogue — ou déjà vue
/// plus haut dans le même fichier.
/// <para>
/// Une fiche qui a un identifiant (ISBN, code-barres) n'est comparée que par
/// lui : deux éditions d'un même roman ont le même titre et le même auteur,
/// et ce ne sont pas des doublons. Sans identifiant, on compare type, titre
/// et auteur, sans tenir compte de la casse ni des espaces en bordure.
/// </para>
/// </summary>
public sealed class DuplicateDetector
{
    private readonly HashSet<string> _existing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inFile = new(StringComparer.Ordinal);

    /// <summary>Inscrit une fiche déjà au catalogue.</summary>
    public void AddExisting(MediaType type, string title, string? creator, string? isbn, string? barcode)
    {
        foreach (var key in AllKeys(type, title, creator, isbn, barcode))
        {
            _existing.Add(key);
        }
    }

    /// <summary>
    /// Juge une fiche du fichier, et la retient pour juger les suivantes.
    /// Rend <c>null</c> si elle est nouvelle.
    /// </summary>
    public DuplicateReason? Check(MediaType type, string title, string? creator, MediaSpecifics? specifics)
    {
        var (key, reason) = IdentityKey(type, title, creator, Isbn(specifics), Barcode(specifics));

        if (_existing.Contains(key))
        {
            return reason;
        }

        return _inFile.Add(key) ? null : DuplicateReason.RepeatedInFile;
    }

    /// <summary>
    /// Clé par laquelle une fiche entrante est comparée : son identifiant
    /// s'il en a un, sinon type + titre + auteur.
    /// </summary>
    private static (string Key, DuplicateReason Reason) IdentityKey(
        MediaType type, string title, string? creator, string? isbn, string? barcode)
    {
        if (CanonicalIsbn(isbn) is { } canonicalIsbn)
        {
            return ($"isbn:{canonicalIsbn}", DuplicateReason.SameIsbn);
        }

        if (BarcodeFormat.Normalize(barcode) is { } canonicalBarcode)
        {
            return ($"barcode:{canonicalBarcode}", DuplicateReason.SameBarcode);
        }

        return (TitleKey(type, title, creator), DuplicateReason.SameTitle);
    }

    /// <summary>
    /// Une fiche existante est inscrite sous toutes ses clés : une fiche
    /// entrante sans ISBN doit la retrouver par son titre, même si elle, en
    /// a un.
    /// </summary>
    private static IEnumerable<string> AllKeys(
        MediaType type, string title, string? creator, string? isbn, string? barcode)
    {
        yield return TitleKey(type, title, creator);

        if (CanonicalIsbn(isbn) is { } canonicalIsbn)
        {
            yield return $"isbn:{canonicalIsbn}";
        }

        if (BarcodeFormat.Normalize(barcode) is { } canonicalBarcode)
        {
            yield return $"barcode:{canonicalBarcode}";
        }
    }

    private static string TitleKey(MediaType type, string title, string? creator) =>
        $"title:{type}|{title.Trim().ToLowerInvariant()}|{creator?.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Une seule forme par ISBN : la forme 13 quand elle existe, pour qu'un
    /// livre saisi avec l'ISBN-10 retrouve son EAN-13.
    /// </summary>
    private static string? CanonicalIsbn(string? isbn)
    {
        var forms = IsbnFormat.Equivalents(isbn);
        return forms.FirstOrDefault(f => f.Length == 13) ?? forms.FirstOrDefault();
    }

    private static string? Isbn(MediaSpecifics? specifics) => specifics switch
    {
        BookSpecifics book => book.Isbn,
        ComicBookSpecifics comic => comic.Isbn,
        _ => null
    };

    private static string? Barcode(MediaSpecifics? specifics) =>
        specifics is MusicSpecifics music ? music.Barcode : null;
}
