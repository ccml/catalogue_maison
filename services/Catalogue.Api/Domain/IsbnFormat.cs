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

    /// <summary>
    /// Vrai si le code, une fois normalisé, est un ISBN : 10 caractères, ou
    /// un EAN-13 du préfixe livre 978 / 979.
    /// </summary>
    public static bool IsIsbn(string? normalized) =>
        normalized is { Length: 10 }
        || (normalized is { Length: 13 }
            && (normalized.StartsWith("978", StringComparison.Ordinal)
                || normalized.StartsWith("979", StringComparison.Ordinal)));

    /// <summary>
    /// Les formes sous lesquelles un même ISBN a pu être saisi : lui-même,
    /// et son équivalent 10 ↔ 13 quand il existe. Un livre enregistré avec
    /// l'ISBN-10 imprimé au dos doit être retrouvé par le scan de son
    /// EAN-13, et réciproquement.
    /// </summary>
    public static IReadOnlyList<string> Equivalents(string? raw)
    {
        var normalized = Normalize(raw);
        if (!IsIsbn(normalized))
        {
            return normalized is null ? [] : [normalized];
        }

        var other = normalized!.Length == 10 ? ToIsbn13(normalized) : ToIsbn10(normalized);
        return other is null ? [normalized] : [normalized, other];
    }

    private static string? ToIsbn13(string isbn10)
    {
        if (isbn10[..9].Any(c => !char.IsAsciiDigit(c)))
        {
            return null;
        }

        var stem = "978" + isbn10[..9];
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (stem[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return stem + (char)('0' + (10 - sum % 10) % 10);
    }

    private static string? ToIsbn10(string isbn13)
    {
        if (!isbn13.StartsWith("978", StringComparison.Ordinal) || isbn13.Any(c => !char.IsAsciiDigit(c)))
        {
            return null;
        }

        var stem = isbn13[3..12];
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (10 - i) * (stem[i] - '0');
        }

        var check = (11 - sum % 11) % 11;
        return stem + (check == 10 ? 'X' : (char)('0' + check));
    }
}
