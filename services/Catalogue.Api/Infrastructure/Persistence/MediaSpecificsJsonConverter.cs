using System.Text.Json;
using System.Text.Json.Serialization;
using Catalogue.Api.Domain;

namespace Catalogue.Api.Infrastructure.Persistence;

/// <summary>
/// Sérialisation polymorphe des spécificités, indépendante de l'ordre des
/// clés.
/// <para>
/// La polymorphie native de <c>System.Text.Json</c>
/// (<c>JsonPolymorphic</c>) exige que le discriminant soit la
/// <strong>première</strong> propriété du document. PostgreSQL ne peut pas
/// tenir cette garantie : une colonne <c>jsonb</c> est stockée décomposée et
/// restitue ses clés triées par longueur puis par octets, pas dans l'ordre
/// d'écriture. Un livre écrit <c>{"type":"book","isbn":…}</c> revient
/// <c>{"isbn":…,"type":"book"}</c>, et le discriminant n'est plus reconnu.
/// </para>
/// <para>
/// Ce convertisseur lit le discriminant où qu'il se trouve. Il est utilisé
/// aussi bien pour le stockage que pour la frontière HTTP, afin qu'un seul
/// mécanisme gouverne la forme du document — deux seraient deux occasions de
/// divergence. Ne pas revenir à <c>JsonPolymorphic</c> tant que les
/// spécificités vivent dans du <c>jsonb</c>.
/// </para>
/// </summary>
public sealed class MediaSpecificsJsonConverter : JsonConverter<MediaSpecifics>
{
    /// <summary>
    /// Nom du discriminant. Il fait partie du contrat HTTP et du contrat de
    /// stockage : le changer invaliderait les documents déjà écrits.
    /// </summary>
    public const string DiscriminatorName = "type";

    private static readonly (string Discriminator, Type Type)[] Mapping =
    [
        ("book", typeof(BookSpecifics)),
        ("comic", typeof(ComicBookSpecifics)),
        ("music", typeof(MusicSpecifics)),
        ("film", typeof(FilmSpecifics)),
        ("game", typeof(GameSpecifics))
    ];

    private static readonly Dictionary<string, Type> ByDiscriminator =
        Mapping.ToDictionary(entry => entry.Discriminator, entry => entry.Type, StringComparer.Ordinal);

    private static readonly Dictionary<Type, string> ByType =
        Mapping.ToDictionary(entry => entry.Type, entry => entry.Discriminator);

    public override MediaSpecifics? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Les spécificités d'un média doivent être un objet JSON.");
        }

        if (!root.TryGetProperty(DiscriminatorName, out var discriminator)
            || discriminator.ValueKind != JsonValueKind.String)
        {
            throw new JsonException(
                $"Les spécificités ne portent pas de discriminant « {DiscriminatorName} » exploitable.");
        }

        var name = discriminator.GetString();
        if (name is null || !ByDiscriminator.TryGetValue(name, out var target))
        {
            throw new JsonException($"Type de spécificités inconnu : « {name} ».");
        }

        // Le type concret n'est pas géré par ce convertisseur : pas de
        // récursion.
        return (MediaSpecifics?)root.Deserialize(target, options);
    }

    public override void Write(Utf8JsonWriter writer, MediaSpecifics value, JsonSerializerOptions options)
    {
        var runtimeType = value.GetType();

        if (!ByType.TryGetValue(runtimeType, out var discriminator))
        {
            throw new JsonException(
                $"Le type de spécificités « {runtimeType.Name} » n'est pas déclaré dans le convertisseur.");
        }

        writer.WriteStartObject();
        writer.WriteString(DiscriminatorName, discriminator);

        using var document = JsonSerializer.SerializeToDocument(value, runtimeType, options);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals(DiscriminatorName))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
