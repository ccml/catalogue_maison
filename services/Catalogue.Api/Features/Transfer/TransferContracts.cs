using Catalogue.Api.Domain;

namespace Catalogue.Api.Features.Transfer;

/// <summary>
/// Fichier d'export d'un catalogue, relu tel quel par l'import. Ne porte
/// <strong>ni identifiant de fiche, ni ménage</strong> : il doit pouvoir être
/// importé chez un autre ménage, et l'identifiant du ménage (= nom de schéma)
/// n'est jamais exposé à un membre.
/// </summary>
public sealed record CatalogueExport(
    string Format,
    int Version,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ExportedMedia> Medias)
{
    /// <summary>Marque le fichier comme un export de CatalogueMaison.</summary>
    public const string FormatName = "cataloguemaison";

    /// <summary>
    /// Version du format. À incrémenter sur tout changement que l'import
    /// d'une version antérieure ne saurait pas relire.
    /// </summary>
    public const int CurrentVersion = 1;
}

/// <summary>Une fiche exportée : tout ce qu'elle porte, sauf son identifiant.</summary>
public sealed record ExportedMedia(
    MediaType Type,
    string? Title,
    string? Creator = null,
    int? Year = null,
    string? Location = null,
    bool IsLent = false,
    DateTimeOffset? AddedAt = null,
    MediaSpecifics? Specifics = null);

/// <summary>Bilan d'un import : ce qui a été ajouté, ce qui a été sauté et pourquoi.</summary>
public sealed record ImportReport(int Imported, IReadOnlyList<SkippedMedia> Skipped);

/// <summary>
/// Fiche du fichier non importée parce qu'elle fait doublon.
/// <see cref="Position"/> part de 1, comme on compte les fiches à l'écran.
/// </summary>
public sealed record SkippedMedia(int Position, MediaType Type, string Title, DuplicateReason Reason);

/// <summary>Pourquoi une fiche est tenue pour un doublon.</summary>
public enum DuplicateReason
{
    /// <summary>Le catalogue a déjà un livre ou une BD de même ISBN (forme 10 ou 13).</summary>
    SameIsbn,

    /// <summary>Le catalogue a déjà un disque de même code-barres.</summary>
    SameBarcode,

    /// <summary>
    /// Sans ISBN ni code-barres : le catalogue a déjà une fiche de même type,
    /// même titre et même auteur.
    /// </summary>
    SameTitle,

    /// <summary>Une fiche précédente du même fichier est déjà la même.</summary>
    RepeatedInFile
}
