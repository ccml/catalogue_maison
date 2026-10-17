using System.Text.RegularExpressions;

namespace Tenants.Api.Infrastructure.Tenancy;

/// <summary>
/// Identifiant du ménage, tel que le porte le claim <c>tenant</c>.
/// <para>
/// Même règle de forme que dans Catalogue.Api, où cette valeur devient un
/// nom de schéma : ce service l'écrit dans l'attribut Keycloak dont naît le
/// claim, il ne doit donc rien y écrire que Catalogue.Api refuserait. Ne pas
/// assouplir l'une sans l'autre.
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
