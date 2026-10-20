using System.Security.Cryptography;
using System.Text;

namespace Tenants.Api.Features.Sharing;

/// <summary>
/// Code d'invitation au partage : dix caractères tirés au hasard, présentés
/// <c>XXXXX-XXXXX</c>, que l'administrateur transmet lui-même à l'autre
/// ménage.
/// <para>
/// L'alphabet écarte ce qui se confond à la lecture ou à la dictée (I, L, O,
/// 0, 1) : 31 symboles, soit environ 8 × 10¹⁴ codes. Seule l'empreinte est
/// stockée ; le code n'est montré qu'à sa création.
/// </para>
/// </summary>
public static class InvitationCode
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Length = 10;

    /// <summary>Un nouveau code, sous sa forme affichée.</summary>
    public static string Generate()
    {
        var symbols = RandomNumberGenerator.GetItems(Alphabet.AsSpan(), Length);
        return $"{new string(symbols[..5])}-{new string(symbols[5..])}";
    }

    /// <summary>
    /// Forme canonique d'un code saisi : majuscules, sans tiret ni espace.
    /// <c>null</c> si ce ne peut pas être un code — inutile alors de le
    /// chercher.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var builder = new StringBuilder(Length);
        foreach (var c in input)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c);
            if (!Alphabet.Contains(upper) || builder.Length == Length)
            {
                return null;
            }

            builder.Append(upper);
        }

        return builder.Length == Length ? builder.ToString() : null;
    }

    /// <summary>Empreinte stockée d'un code canonique.</summary>
    public static string Hash(string normalized) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(normalized)));
}
