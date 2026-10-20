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

    /// <summary>Auteur, réalisateur, artiste ou scénariste : même rôle sémantique.</summary>
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
    /// Colonne générée depuis <c>specifics -&gt; 'barcode'</c>, indexée : un
    /// disque déjà possédé est reconnu au scan par son code-barres. Jamais
    /// écrite depuis le C#.
    /// </summary>
    public string? Barcode { get; private set; }

    /// <summary>
    /// Colonne générée depuis <c>specifics -&gt; 'series'</c>, indexée : on
    /// cherche une BD par sa série avant de la chercher par le titre de
    /// l'album. Jamais écrite depuis le C#.
    /// </summary>
    public string? Series { get; private set; }

    /// <summary>
    /// Colonne générée depuis <c>specifics -&gt; 'volume'</c> : sans elle, les
    /// albums d'une série ne se classeraient pas par tome. Jamais écrite
    /// depuis le C#.
    /// </summary>
    public int? Volume { get; private set; }

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
        var cleanTitle = Validate(type, title, year, specifics, addedAt);

        return new Media
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Title = cleanTitle,
            Creator = Blank(creator),
            Year = year,
            Location = Blank(location),
            IsLent = false,
            Specifics = Canonicalize(specifics),
            AddedAt = addedAt
        };
    }

    /// <summary>
    /// Recrée une fiche venue d'un export : date d'ajout et état « prêté »
    /// d'origine conservés, nouvel identifiant. Les règles sont celles de la
    /// saisie, à ceci près que l'année se juge par rapport à
    /// <paramref name="now"/> et non à la date d'ajout — une fiche corrigée
    /// après son ajout doit pouvoir revenir telle quelle.
    /// </summary>
    /// <exception cref="MediaValidationException">Si la fiche est invalide.</exception>
    public static Media Import(
        MediaType type,
        string? title,
        string? creator,
        int? year,
        string? location,
        bool isLent,
        MediaSpecifics? specifics,
        DateTimeOffset? addedAt,
        DateTimeOffset now)
    {
        if (addedAt > now)
        {
            throw new MediaValidationException(["La date d'ajout est dans le futur."]);
        }

        var cleanTitle = Validate(type, title, year, specifics, now);

        return new Media
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Title = cleanTitle,
            Creator = Blank(creator),
            Year = year,
            Location = Blank(location),
            IsLent = isLent,
            Specifics = Canonicalize(specifics),
            AddedAt = addedAt ?? now
        };
    }

    /// <summary>
    /// Corrige une fiche existante, <strong>type compris</strong> : prendre
    /// un album de BD pour un livre est une erreur de saisie banale, et elle
    /// doit se réparer sans supprimer puis resaisir. Les spécificités doivent
    /// alors correspondre au nouveau type.
    /// <para>
    /// Ne touche ni à <see cref="AddedAt"/>, qui date l'entrée dans le
    /// catalogue et non la dernière correction, ni à <see cref="IsLent"/>,
    /// qui a son propre geste : corriger une fiche ne doit pas faire revenir
    /// un média prêté.
    /// </para>
    /// </summary>
    /// <exception cref="MediaValidationException">Si la fiche est invalide.</exception>
    public void Update(
        MediaType type,
        string? title,
        string? creator,
        int? year,
        string? location,
        MediaSpecifics? specifics,
        DateTimeOffset now)
    {
        var cleanTitle = Validate(type, title, year, specifics, now);

        Type = type;
        Title = cleanTitle;
        Creator = Blank(creator);
        Year = year;
        Location = Blank(location);
        Specifics = Canonicalize(specifics);
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
        List<string> errors = [];
        CheckSpecifics(Type, specifics, errors);

        if (errors.Count > 0)
        {
            throw new MediaValidationException(errors);
        }

        Specifics = Canonicalize(specifics);
    }

    /// <summary>
    /// Valide une fiche et rend son titre nettoyé. Partagée par la création
    /// et la correction : deux jeux de règles divergeraient.
    /// </summary>
    private static string Validate(
        MediaType type,
        string? title,
        int? year,
        MediaSpecifics? specifics,
        DateTimeOffset reference)
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

        if (year is { } value && (value < 1400 || value > reference.Year + 1))
        {
            errors.Add("L'année est hors des bornes plausibles.");
        }

        CheckSpecifics(type, specifics, errors);

        if (errors.Count > 0)
        {
            throw new MediaValidationException(errors);
        }

        return cleanTitle!;
    }

    private static void CheckSpecifics(MediaType type, MediaSpecifics? specifics, List<string> errors)
    {
        if (specifics is null)
        {
            return;
        }

        if (specifics.AppliesTo != type)
        {
            errors.Add(
                $"Les spécificités fournies concernent un média de type {specifics.AppliesTo}, pas {type}.");
        }

        errors.AddRange(specifics.Validate());
    }

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Normalise ce qui doit l'être avant stockage — l'ISBN et le code-barres,
    /// dont dépendent les colonnes générées.
    /// </summary>
    private static MediaSpecifics? Canonicalize(MediaSpecifics? specifics) => specifics switch
    {
        BookSpecifics book => book with { Isbn = IsbnFormat.Normalize(book.Isbn) },
        ComicBookSpecifics comic => comic with { Isbn = IsbnFormat.Normalize(comic.Isbn) },
        MusicSpecifics music => music with { Barcode = BarcodeFormat.Normalize(music.Barcode) },
        _ => specifics
    };
}

/// <summary>Fiche média refusée à l'entrée.</summary>
public sealed class MediaValidationException(IReadOnlyCollection<string> errors)
    : Exception("La fiche du média est invalide.")
{
    public IReadOnlyCollection<string> Errors { get; } = errors;
}
