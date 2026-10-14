using System.Text.Json;
using System.Text.Json.Serialization;
using Catalogue.Api.Domain;

namespace Catalogue.Api.Infrastructure.Persistence;

/// <summary>
/// Sérialisation des spécificités vers <c>jsonb</c>. La politique de nommage
/// est en camelCase et <strong>fait partie du contrat de stockage</strong> :
/// la colonne générée <c>isbn</c> lit <c>specifics -&gt;&gt; 'isbn'</c>. La
/// changer invaliderait cette colonne sur les données déjà saisies.
/// </summary>
public static class MediaSpecificsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new MediaSpecificsJsonConverter() }
    };

    public static string? Serialize(MediaSpecifics? specifics) =>
        specifics is null ? null : JsonSerializer.Serialize(specifics, Options);

    public static MediaSpecifics? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<MediaSpecifics>(json, Options);
}
