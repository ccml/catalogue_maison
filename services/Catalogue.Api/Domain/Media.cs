namespace Catalogue.Api.Domain;

/// <summary>
/// Un média possédé par le ménage. Modèle <strong>hybride</strong> : ce qui
/// est commun et sert à filtrer, trier ou chercher est en colonnes réelles ;
/// ce qui est propre à un type vit dans <see cref="Specifics"/>.
/// </summary>
public class Media
{
    private Media()
    {
        // Réservé à EF Core.
        Title = string.Empty;
    }

    public Guid Id { get; private set; }

    public MediaType Type { get; private set; }

    public string Title { get; private set; }

    /// <summary>Auteur, réalisateur ou artiste : même rôle sémantique.</summary>
    public string? Creator { get; private set; }

    public int? Year { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>Où le média se trouve physiquement dans la maison.</summary>
    public string? Location { get; private set; }

    public bool IsLent { get; private set; }

    public MediaSpecifics? Specifics { get; private set; }

    /// <summary>
    /// Colonne générée par PostgreSQL depuis <c>specifics -&gt; 'isbn'</c>,
    /// indexée pour le chemin « scan ». Jamais écrite depuis le C#.
    /// </summary>
    public string? Isbn { get; private set; }

    /// <summary>
    /// Crée un média. Seuls le type et le titre sont requis : la saisie
    /// manuelle doit rester possible sans rien connaître d'autre.
    /// </summary>
    /// <exception cref="MediaValidationException">Si la fiche est invalide.</exception>
    public static Media Create(
        MediaType type,
        string? title,
        string? creator,
        int? year,
        string? location,
        MediaSpecifics? specifics,
        DateTimeOffset addedAt)
    {
        List<string> errors = [];

        var cleanTitle = title?.Trim();
        if (string.IsNullOrEmpty(cleanTitle))
        {
            errors.Add("Le titre est requis.");
        }
        else if (cleanTitle.Length > 512)
        {
            errors.Add("Le titre ne peut pas dépasser 512 caractères.");
        }

        if (!Enum.IsDefined(type))
        {
            errors.Add("Le type de média est inconnu.");
        }

        if (year is { } y && (y < 1400 || y > addedAt.Year + 1))
        {
            errors.Add("L'année est hors des bornes plausibles.");
        }

        if (specifics is not null)
        {
            if (specifics.AppliesTo != type)
            {
                errors.Add($"Les spécificités fournies concernent un média de type {specifics.AppliesTo}, pas {type}.");
            }

            errors.AddRange(specifics.Validate());
        }

        if (errors.Count > 0)
        {
            throw new MediaValidationException(errors);
        }

        return new Media
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Title = cleanTitle!,
            Creator = Blank(creator),
            Year = year,
            Location = Blank(location),
            IsLent = false,
            Specifics = Canonicalize(specifics),
            AddedAt = addedAt
        };
    }

    /// <summary>Marque le média comme prêté ou revenu.</summary>
    public void SetLent(bool isLent) => IsLent = isLent;

    /// <summary>
    /// Remplace les spécificités, par exemple après un enrichissement validé
    /// par l'utilisateur.
    /// </summary>
    /// <exception cref="MediaValidationException">Si la forme est invalide.</exception>
    public void ReplaceSpecifics(MediaSpecifics? specifics)
    {
        if (specifics is not null)
        {
            List<string> errors = [];
            if (specifics.AppliesTo != Type)
            {
                errors.Add($"Les spécificités fournies concernent un média de type {specifics.AppliesTo}, pas {Type}.");
            }

            errors.AddRange(specifics.Validate());
            if (errors.Count > 0)
            {
                throw new MediaValidationException(errors);
            }
        }

        Specifics = Canonicalize(specifics);
    }

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Normalise ce qui doit l'être avant stockage — l'ISBN en premier, dont
    /// dépend la colonne générée.
    /// </summary>
    private static MediaSpecifics? Canonicalize(MediaSpecifics? specifics) => specifics switch
    {
        BookSpecifics book => book with { Isbn = IsbnFormat.Normalize(book.Isbn) },
        _ => specifics
    };
}

/// <summary>Fiche média refusée à l'entrée.</summary>
public sealed class MediaValidationException(IReadOnlyCollection<string> errors)
    : Exception("La fiche du média est invalide.")
{
    public IReadOnlyCollection<string> Errors { get; } = errors;
}
