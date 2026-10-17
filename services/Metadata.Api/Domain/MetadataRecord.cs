namespace Metadata.Api.Domain;

/// <summary>
/// Nature de l'objet décrit par une notice, dans le vocabulaire de ce
/// service. Ce n'est <strong>pas</strong> le type de média du catalogue :
/// Catalogue.Api le traduit et décide de ce qu'il conserve.
/// </summary>
public enum MetadataKind
{
    Book,
    ComicBook,
    Music
}

/// <summary>
/// Notice d'enrichissement, telle qu'une source l'a décrite. Tous les
/// champs sauf le titre sont facultatifs : une notice partielle vaut mieux
/// que pas de notice, l'utilisateur complète.
/// <para>
/// C'est aussi la forme stockée en cache : y ajouter un champ facultatif est
/// sans risque, en renommer un rend illisibles les notices déjà en cache.
/// </para>
/// </summary>
public sealed record MetadataRecord
{
    public required MetadataKind Kind { get; init; }

    public required string Title { get; init; }

    /// <summary>Auteurs, scénaristes ou artistes, dans l'ordre de la source.</summary>
    public IReadOnlyList<string> Creators { get; init; } = [];

    public int? Year { get; init; }

    public string? Publisher { get; init; }

    public int? Pages { get; init; }

    /// <summary>ISBN-13, pour un livre ou une BD.</summary>
    public string? Isbn { get; init; }

    public string? Series { get; init; }

    public int? Volume { get; init; }

    public string? Illustrator { get; init; }

    public string? Label { get; init; }

    /// <summary>Support d'un album : CD, vinyle…</summary>
    public string? Format { get; init; }

    public IReadOnlyList<string> Tracks { get; init; } = [];

    /// <summary>Identifiant de la source qui a fourni la notice (<c>bnf</c>, <c>openlibrary</c>…).</summary>
    public required string Source { get; init; }
}
