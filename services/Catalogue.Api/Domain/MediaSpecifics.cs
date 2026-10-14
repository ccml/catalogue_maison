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

public sealed record BookSpecifics : MediaSpecifics
{
    public string? Isbn { get; init; }
    public string? Publisher { get; init; }
    public int? Pages { get; init; }

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
    }
}

public sealed record MusicSpecifics : MediaSpecifics
{
    public string? Label { get; init; }
    public string? Format { get; init; }
    public IReadOnlyList<string> Tracks { get; init; } = [];

    [JsonIgnore]
    public override MediaType AppliesTo => MediaType.Music;

    public override IEnumerable<string> Validate()
    {
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

    [JsonIgnore]
    public override MediaType AppliesTo => MediaType.Film;

    public override IEnumerable<string> Validate()
    {
        if (DurationMinutes is <= 0)
        {
            yield return "La durée doit être strictement positive.";
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
