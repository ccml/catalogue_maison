using System.Text.RegularExpressions;

namespace Metadata.Api.Domain;

/// <summary>
/// Identifiant IMDb d'un film (<c>tt1375666</c>), clé de sa fiche et de son
/// cache. Validé strictement : il finit dans l'URL d'une source externe.
/// </summary>
public sealed partial record FilmId
{
    private FilmId(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string? raw, out FilmId? id)
    {
        id = null;
        var candidate = raw?.Trim().ToLowerInvariant();

        if (candidate is null || !Shape().IsMatch(candidate))
        {
            return false;
        }

        id = new FilmId(candidate);
        return true;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^tt\d{7,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
