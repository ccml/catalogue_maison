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
    Music,
    Film
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

    /// <summary>Série de l'œuvre (Harry Potter, Blacksad), pas la collection de l'éditeur.</summary>
    public string? Series { get; init; }

    /// <summary>Rang dans la série.</summary>
    public int? Volume { get; init; }

    /// <summary>
    /// Mention d'édition, telle qu'imprimée (« Éd. de luxe », « Nouvelle
    /// éd. ») : distingue deux objets qui portent le même titre.
    /// </summary>
    public string? Edition { get; init; }

    /// <summary>Collection de l'éditeur (Folio policier, Pléiade).</summary>
    public string? Collection { get; init; }

    /// <summary>Numéro dans la collection. Du texte : certaines collections numérotent « LP 3121 ».</summary>
    public string? CollectionNumber { get; init; }

    /// <summary>Genre, sous un libellé unique (« Roman », « Policier »…), quand la source le dit.</summary>
    public string? Genre { get; init; }

    public string? Illustrator { get; init; }

    public string? Label { get; init; }

    /// <summary>Support d'un album : CD, vinyle…</summary>
    public string? Format { get; init; }

    public IReadOnlyList<string> Tracks { get; init; } = [];

    /// <summary>Durée d'un film, en minutes.</summary>
    public int? DurationMinutes { get; init; }

    /// <summary>Acteurs principaux d'un film, dans l'ordre de la source.</summary>
    public IReadOnlyList<string> Actors { get; init; } = [];

    /// <summary>
    /// Vrai pour une notice provisoire : décrite d'après les déclarations de
    /// l'éditeur, avant catalogage. Elle est souvent incomplète — en
    /// particulier, rien n'y dit qu'une BD est une BD. Elle est gardée moins
    /// longtemps en cache, pour être remplacée dès que la notice définitive
    /// existe, et Catalogue.Api en avertit l'utilisateur.
    /// </summary>
    public bool Provisional { get; init; }

    /// <summary>Identifiant de la source qui a fourni la notice (<c>bnf</c>, <c>openlibrary</c>…).</summary>
    public required string Source { get; init; }
}
