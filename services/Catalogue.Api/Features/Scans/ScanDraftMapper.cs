using Catalogue.Api.Domain;
using Catalogue.Api.Infrastructure.Metadata;

namespace Catalogue.Api.Features.Scans;

/// <summary>
/// Traduit une notice de Metadata.Api en fiche du catalogue. C'est ici que
/// Catalogue.Api <strong>décide de ce qu'il conserve</strong> : une source
/// externe n'est pas une entrée de confiance, et ce qu'elle dit passe par
/// les mêmes bornes que la saisie manuelle.
/// </summary>
public static class ScanDraftMapper
{
    private const int MaxTitleLength = 512;
    private const int MaxTextLength = 256;

    /// <summary>
    /// Fiche tirée d'une notice, ou <c>null</c> si la notice ne décrit rien
    /// d'exploitable (type inconnu, titre absent).
    /// </summary>
    public static ScanDraft? FromRecord(MetadataRecordDto record, string code, DateTimeOffset now)
    {
        var title = Text(record.Title, MaxTitleLength);
        if (title is null)
        {
            return null;
        }

        var creators = record.Creators
            .Select(c => Text(c, MaxTextLength))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var creator = creators.Count == 0 ? null : Text(string.Join(", ", creators), MaxTitleLength);
        var year = record.Year is { } y && y >= 1400 && y <= now.Year + 1 ? y : (int?)null;

        // Une notice qui ne porte pas d'ISBN garde celui qui a été scanné.
        var isbn = IsbnFormat.Normalize(record.Isbn) ?? (IsbnFormat.IsIsbn(code) ? code : null);

        MediaSpecifics? specifics = record.Kind switch
        {
            "Book" => new BookSpecifics
            {
                Isbn = isbn,
                Publisher = Text(record.Publisher, MaxTextLength),
                Pages = Positive(record.Pages)
            },
            "ComicBook" => new ComicBookSpecifics
            {
                Series = Text(record.Series, MaxTextLength),
                Volume = Positive(record.Volume),
                Illustrator = Text(record.Illustrator, MaxTextLength),
                Publisher = Text(record.Publisher, MaxTextLength),
                Isbn = isbn
            },
            "Music" => new MusicSpecifics
            {
                Label = Text(record.Label, MaxTextLength),
                Format = Text(record.Format, MaxTextLength),
                Tracks = record.Tracks.Select(t => Text(t, MaxTitleLength)).OfType<string>().ToList()
            },
            _ => null
        };

        if (specifics is null)
        {
            return null;
        }

        // Dernier filet : la fiche proposée doit être une fiche que l'API
        // accepterait. Si ce n'est pas le cas, on n'en garde que les
        // colonnes communes plutôt que de proposer un refus à l'utilisateur.
        if (specifics.Validate().Any())
        {
            specifics = null;
        }

        return new ScanDraft(specifics?.AppliesTo ?? TypeOf(record.Kind), title, creator, year, specifics);
    }

    /// <summary>
    /// Fiche minimale quand aucune notice n'est disponible : un ISBN suffit
    /// à dire qu'il s'agit d'un livre, et à ne pas le faire ressaisir. Pour
    /// un autre code-barres, on ne sait rien du type.
    /// </summary>
    public static ScanDraft? FromCodeOnly(string code) =>
        IsbnFormat.IsIsbn(code)
            ? new ScanDraft(MediaType.Book, null, null, null, new BookSpecifics { Isbn = code })
            : null;

    private static MediaType TypeOf(string kind) => kind switch
    {
        "ComicBook" => MediaType.ComicBook,
        "Music" => MediaType.Music,
        _ => MediaType.Book
    };

    private static string? Text(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    private static int? Positive(int? value) => value is > 0 ? value : null;
}
