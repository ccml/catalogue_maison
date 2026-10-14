namespace Catalogue.Api.Domain;

/// <summary>
/// Normalisation des ISBN. L'ISBN est la clé du chemin « scan » : deux
/// saisies du même livre doivent produire la même valeur, sinon la détection
/// de doublon et la recherche par code-barres passent à côté.
/// </summary>
public static class IsbnFormat
{
    /// <summary>
    /// Retire séparateurs et espaces, met en majuscules. Ne juge pas de la
    /// validité : voir <see cref="IsWellFormed"/>.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var buffer = new char[raw.Length];
        var length = 0;
        foreach (var c in raw)
        {
            if (char.IsAsciiDigit(c))
            {
                buffer[length++] = c;
            }
            else if (c is 'x' or 'X')
            {
                buffer[length++] = 'X';
            }
        }

        return length == 0 ? null : new string(buffer, 0, length);
    }

    /// <summary>
    /// Vrai si la forme est plausible : 10 ou 13 caractères significatifs.
    /// La clé de contrôle n'est pas vérifiée — un ISBN mal imprimé ne doit
    /// pas empêcher d'ajouter un livre qu'on a dans les mains.
    /// </summary>
    public static bool IsWellFormed(string? raw)
    {
        var normalized = Normalize(raw);
        return normalized?.Length is 10 or 13;
    }
}
