namespace Catalogue.Api.Domain;

/// <summary>
/// Normalisation des codes-barres commerciaux (EAN-13, EAN-8, UPC-A), clé du
/// chemin « scan » pour ce qui n'a pas d'ISBN — un disque d'abord.
/// <para>
/// Un même disque peut porter un UPC-A de 12 chiffres ou l'EAN-13 qui lui
/// équivaut, préfixé d'un zéro, selon le marché et le lecteur qui l'a lu. Le
/// code est donc stocké sous <strong>une seule forme</strong>, l'EAN-13 : deux
/// saisies du même disque produisent la même valeur, et la détection de
/// doublon par la colonne générée n'a besoin que d'une égalité.
/// </para>
/// </summary>
public static class BarcodeFormat
{
    /// <summary>
    /// Forme canonique : séparateurs retirés, UPC-A ramené à son EAN-13. Ne
    /// juge pas de la validité : voir <see cref="IsWellFormed"/>.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var compact = string.Concat(raw.Where(c => c is not (' ' or '-')));
        if (compact.Length == 0)
        {
            return null;
        }

        return compact.Length == 12 && compact.All(char.IsAsciiDigit) ? "0" + compact : compact;
    }

    /// <summary>
    /// Vrai si la forme est plausible : chiffres seuls, 8, 12 ou 13. La clé de
    /// contrôle n'est pas vérifiée — un code mal imprimé ne doit pas empêcher
    /// d'enregistrer un disque qu'on a dans les mains.
    /// </summary>
    public static bool IsWellFormed(string? raw)
    {
        var normalized = Normalize(raw);
        return normalized is { Length: 8 or 13 } && normalized.All(char.IsAsciiDigit);
    }
}
