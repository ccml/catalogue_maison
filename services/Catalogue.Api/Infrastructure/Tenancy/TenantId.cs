using System.Text.RegularExpressions;

namespace Catalogue.Api.Infrastructure.Tenancy;

/// <summary>
/// Identifiant du ménage, et donc nom du schéma PostgreSQL adressé.
/// <para>
/// Cette valeur finit dans un identifiant SQL. La validation ci-dessous est
/// une <strong>frontière de sécurité</strong>, pas une commodité : ne pas
/// l'assouplir, et ne jamais construire un <c>TenantId</c> autrement que par
/// ces fabriques.
/// </para>
/// </summary>
public sealed partial class TenantId : IEquatable<TenantId>
{
    public const int MaxLength = 48;

    private static readonly string[] Reserved = ["public", "information_schema", "template"];

    private TenantId(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? raw, out TenantId? tenant)
    {
        tenant = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = raw.Trim();

        if (candidate.Length > MaxLength
            || !ShapePattern().IsMatch(candidate)
            || Reserved.Contains(candidate, StringComparer.Ordinal)
            || candidate.StartsWith("pg_", StringComparison.Ordinal))
        {
            return false;
        }

        tenant = new TenantId(candidate);
        return true;
    }

    public static TenantId Create(string? raw) =>
        TryCreate(raw, out var tenant) && tenant is not null
            ? tenant
            : throw new ArgumentException($"Identifiant de ménage invalide : « {raw} ».", nameof(raw));

    public bool Equals(TenantId? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TenantId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ShapePattern();
}
