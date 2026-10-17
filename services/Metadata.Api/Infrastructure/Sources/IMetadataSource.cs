using Metadata.Api.Domain;

namespace Metadata.Api.Infrastructure.Sources;

/// <summary>
/// Une API externe capable de décrire un code. Les sources sont interrogées
/// dans leur ordre d'enregistrement ; la première qui répond l'emporte.
/// </summary>
public interface IMetadataSource
{
    /// <summary>Identifiant stable, repris dans <see cref="MetadataRecord.Source"/> et dans le cache.</summary>
    string Name { get; }

    bool Supports(ProductCode code);

    /// <summary>
    /// Notice trouvée, ou <c>null</c> si la source ne connaît pas ce code.
    /// </summary>
    /// <exception cref="MetadataSourceException">
    /// Si la source n'a pas pu répondre : réseau, quota, réponse illisible.
    /// À distinguer d'un code inconnu, qui seul peut être mis en cache.
    /// </exception>
    Task<MetadataRecord?> LookupAsync(ProductCode code, CancellationToken cancellationToken);
}

/// <summary>La source n'a pas pu répondre. Rien n'en est déduit sur le code.</summary>
public sealed class MetadataSourceException(string source, string message, Exception? inner = null)
    : Exception($"Source « {source} » : {message}", inner)
{
    public string SourceName { get; } = source;
}
