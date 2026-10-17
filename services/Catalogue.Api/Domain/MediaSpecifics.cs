using System.Text.Json.Serialization;

namespace Catalogue.Api.Domain;

/// <summary>
/// Partie du média propre à son type, stockée en <c>jsonb</c>.
/// PostgreSQL ne valide pas la forme du document : elle est garantie ici, par
/// un type par média et une validation à l'entrée. Aucune écriture de JSON
/// brut nulle part.
/// </summary>
/// <remarks>
/// Le discriminant est géré par <c>MediaSpecificsJsonConverter</c>, et non
/// par <c>JsonPolymorphic</c> : voir ce convertisseur pour la raison.
/// </remarks>
public abstract record MediaSpecifics
{
    /// <summary>Type de média auquel cette forme s'applique.</summary>
    [JsonIgnore]
    public abstract MediaType AppliesTo { get; }

    /// <summary>Messages d'erreur, vide si la forme est valide.</summary>
    public abstract IEnumerable<string> Validate();
}

/// <summary>
/// Un livre. La <strong>série</strong> (Harry Potter, tome 3) et la
/// <strong>collection</strong> de l'éditeur (Folio policier, n° 85) sont deux
/// choses distinctes, qu'une couverture confond volontiers : la première
/// appartient à l'œuvre, la seconde à l'édition.
/// </summary>
public sealed record BookSpecifics : MediaSpecifics
{
    /// <summary>Longueur maximale d'un libellé : celle de la colonne générée <c>series</c>.</summary>
    public const int MaxLabelLength = 256;

    public string? Isbn { get; init; }
    public string? Publisher { get; init; }
    public int? Pages { get; init; }

    /// <summary>
    /// Série de l'œuvre. Même clé JSON que la série d'une BD : la colonne
    /// générée <c>series</c> la lit, et la recherche par série vaut donc pour
    /// les livres sans aucune migration.
    /// </summary>
    public string? Series { get; init; }

    /// <summary>Rang dans la série.</summary>
    public int? Volume { get; init; }

    /// <summary>Collection de l'éditeur.</summary>
    public string? Collection { get; init; }

    /// <summary>Numéro dans la collection. Du texte : certaines collections numérotent « LP 3121 ».</summary>
    public string? CollectionNumber { get; init; }

    /// <summary>Genre, libre ; le frontend propose des libellés pour qu'ils restent homogènes.</summary>
    public string? Genre { get; init; }

    [JsonIgnore]
    public override MediaType AppliesTo => MediaType.Book;

    public override IEnumerable<string> Validate()
    {
        if (Isbn is not null && Isbn.Length > 0 && !IsbnFormat.IsWellFormed(Isbn))
        {
            yield return "L'ISBN doit comporter 10 ou 13 caractères significatifs.";
        }

        if (Pages is <= 0)
        {
            yield return "Le nombre de pages doit être strictement positif.";
        }

        if (Volume is <= 0)
        {
            yield return "Le numéro de tome doit être strictement positif.";
        }

        if (Series?.Length > MaxLabelLength || Collection?.Length > MaxLabelLength || Genre?.Length > MaxLabelLength)
        {
            yield return $"La série, la collection et le genre ne peuvent pas dépasser {MaxLabelLength} caractères.";
        }

        if (CollectionNumber?.Length > 32)
        {
            yield return "Le numéro dans la collection ne peut pas dépasser 32 caractères.";
        }
    }
}

/// <summary>
/// Une BD se range par <strong>série et tome</strong> : c'est ainsi qu'on
/// cherche à savoir si on possède déjà un album. Le scénariste vit dans la
/// colonne commune <c>Creator</c>, qui porte le même rôle sémantique pour
/// tous les types ; le dessinateur, propre à la BD, est ici.
/// </summary>
public sealed record ComicBookSpecifics : MediaSpecifics
{
    public string? Series { get; init; }
    public int? Volume { get; init; }
    public string? Illustrator { get; init; }
    public string? Publisher { get; init; }
    public string? Isbn { get; init; }

    /// <summary>Collection de l'éditeur (Aire libre, Shonen manga).</summary>
    public string? Collection { get; init; }

    /// <summary>Numéro dans la collection, en texte comme pour un livre.</summary>
    public string? CollectionNumber { get; init; }

    /// <summary>
    /// Mention d'édition (« Éd. de luxe », « Nouvelle éd. »). Pour une BD,
    /// elle distingue deux objets de même titre qu'un collectionneur ne
    /// confond pas.
    /// </summary>
    public string? Edition { get; init; }

    [JsonIgnore]
    public override MediaType AppliesTo => MediaType.ComicBook;

    public override IEnumerable<string> Validate()
    {
        if (Isbn is not null && Isbn.Length > 0 && !IsbnFormat.IsWellFormed(Isbn))
        {
            yield return "L'ISBN doit comporter 10 ou 13 caractères significatifs.";
        }

        if (Volume is <= 0)
        {
            yield return "Le numéro de tome doit être strictement positif.";
        }

        // La colonne générée series est bornée : une série plus longue
        // ferait échouer l'écriture en base plutôt qu'à l'entrée.
        if (Series?.Length > BookSpecifics.MaxLabelLength
            || Collection?.Length > BookSpecifics.MaxLabelLength
            || Edition?.Length > BookSpecifics.MaxLabelLength)
        {
            yield return $"La série, la collection et l'édition ne peuvent pas dépasser {BookSpecifics.MaxLabelLength} caractères.";
        }

        if (CollectionNumber?.Length > 32)
        {
            yield return "Le numéro dans la collection ne peut pas dépasser 32 caractères.";
        }
    }
}

public sealed record MusicSpecifics : MediaSpecifics
{
    public string? Label { get; init; }
    public string? Format { get; init; }
    public IReadOnlyList<string> Tracks { get; init; } = [];

    /// <summary>
    /// Code-barres du disque (EAN-13 ou UPC-A), stocké sous sa forme EAN-13.
    /// Promu en colonne générée indexée : c'est par lui qu'un disque déjà
    /// possédé est reconnu au scan, comme un livre par son ISBN.
    /// </summary>
    public string? Barcode { get; init; }

    [JsonIgnore]
    public override MediaType AppliesTo => MediaType.Music;

    public override IEnumerable<string> Validate()
    {
        if (Barcode is not null && Barcode.Length > 0 && !BarcodeFormat.IsWellFormed(Barcode))
        {
            yield return "Le code-barres doit comporter 8, 12 ou 13 chiffres.";
        }

        if (Tracks.Any(string.IsNullOrWhiteSpace))
        {
            yield return "Une piste ne peut pas être vide.";
        }
    }
}

public sealed record FilmSpecifics : MediaSpecifics
{
    public string? Studio { get; init; }
    public int? DurationMinutes { get; init; }
    public string? Support { get; init; }

    /// <summary>Genre, libre ; tel qu'OMDb le donne, il est en anglais (« Action, Sci-Fi »).</summary>
    public string? Genre { get; init; }

    /// <summary>Acteurs principaux. Le réalisateur, lui, est dans la colonne commune <c>Creator</c>.</summary>
    public IReadOnlyList<string> Actors { get; init; } = [];

    [JsonIgnore]
    public override MediaType AppliesTo => MediaType.Film;

    public override IEnumerable<string> Validate()
    {
        if (DurationMinutes is <= 0)
        {
            yield return "La durée doit être strictement positive.";
        }

        if (Genre?.Length > BookSpecifics.MaxLabelLength)
        {
            yield return $"Le genre ne peut pas dépasser {BookSpecifics.MaxLabelLength} caractères.";
        }

        if (Actors.Any(string.IsNullOrWhiteSpace))
        {
            yield return "Un acteur ne peut pas être vide.";
        }
    }
}

public sealed record GameSpecifics : MediaSpecifics
{
    public string? Platform { get; init; }
    public int? MinPlayers { get; init; }
    public int? MaxPlayers { get; init; }

    [JsonIgnore]
    public override MediaType AppliesTo => MediaType.Game;

    public override IEnumerable<string> Validate()
    {
        if (MinPlayers is <= 0 || MaxPlayers is <= 0)
        {
            yield return "Le nombre de joueurs doit être strictement positif.";
        }

        if (MinPlayers is { } min && MaxPlayers is { } max && min > max)
        {
            yield return "Le nombre minimum de joueurs dépasse le maximum.";
        }
    }
}
