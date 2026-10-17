namespace Metadata.Api.Domain;

/// <summary>Nature d'un code lu sur un objet.</summary>
public enum ProductCodeKind
{
    /// <summary>ISBN, qu'il ait été saisi en 10 chiffres ou lu en EAN-13 978 / 979.</summary>
    Isbn,

    /// <summary>Code-barres commercial (EAN-13, EAN-8, UPC-A) qui n'est pas un ISBN.</summary>
    Ean
}

/// <summary>
/// Code-barres ou ISBN normalisé, clé du cache d'enrichissement.
/// <para>
/// Un même livre peut arriver sous trois formes — ISBN-10 imprimé, ISBN-13
/// avec tirets, EAN-13 lu par la caméra. Elles doivent produire la
/// <strong>même</strong> clé, sinon le cache se fragmente et une source est
/// interrogée trois fois pour le même ouvrage. Un ISBN est donc toujours
/// ramené à sa forme 13 chiffres.
/// </para>
/// <para>
/// La clé de contrôle n'est pas exigée : un code mal imprimé ne doit pas
/// empêcher de chercher. Le lecteur de code-barres du frontend la vérifie
/// déjà au moment de la lecture.
/// </para>
/// </summary>
public sealed record ProductCode
{
    private ProductCode(string value, ProductCodeKind kind)
    {
        Value = value;
        Kind = kind;
    }

    /// <summary>Forme normalisée : chiffres seuls, ISBN toujours sur 13 chiffres.</summary>
    public string Value { get; }

    public ProductCodeKind Kind { get; }

    /// <summary>ISBN-10 équivalent, quand il existe (préfixe 978 uniquement).</summary>
    public string? Isbn10 => Kind == ProductCodeKind.Isbn ? ToIsbn10(Value) : null;

    public static bool TryParse(string? raw, out ProductCode? code)
    {
        code = null;

        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 32)
        {
            return false;
        }

        Span<char> buffer = stackalloc char[raw.Length];
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
            else if (c is not ('-' or ' '))
            {
                // Rien d'autre qu'un séparateur n'est toléré : un caractère
                // inattendu est le signe d'une saisie qui n'est pas un code.
                return false;
            }
        }

        var digits = new string(buffer[..length]);

        // Le X n'existe qu'en dernière position d'un ISBN-10.
        var x = digits.IndexOf('X');
        if (x >= 0 && (x != 9 || digits.Length != 10))
        {
            return false;
        }

        code = digits.Length switch
        {
            10 => new ProductCode(ToIsbn13(digits), ProductCodeKind.Isbn),
            13 when digits.StartsWith("978", StringComparison.Ordinal)
                 || digits.StartsWith("979", StringComparison.Ordinal)
                => new ProductCode(digits, ProductCodeKind.Isbn),
            8 or 12 or 13 => new ProductCode(digits, ProductCodeKind.Ean),
            _ => null
        };

        return code is not null;
    }

    public override string ToString() => Value;

    private static string ToIsbn13(string isbn10)
    {
        var stem = "978" + isbn10[..9];
        return stem + Ean13CheckDigit(stem);
    }

    private static string? ToIsbn10(string isbn13)
    {
        if (!isbn13.StartsWith("978", StringComparison.Ordinal))
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
        return stem + (check == 10 ? "X" : check.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static char Ean13CheckDigit(string twelveDigits)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (char)('0' + (10 - sum % 10) % 10);
    }
}
