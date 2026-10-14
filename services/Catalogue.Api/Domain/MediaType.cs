namespace Catalogue.Api.Domain;

/// <summary>
/// Discriminant du média. Ajouter une valeur ne doit demander
/// <strong>aucune migration de schéma</strong> : ce qui est propre à un type
/// vit dans <see cref="Media.Specifics"/>.
/// </summary>
public enum MediaType
{
    Book = 1,
    Music = 2,
    Film = 3,
    Game = 4
}
