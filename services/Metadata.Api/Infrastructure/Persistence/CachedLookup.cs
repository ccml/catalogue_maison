using Metadata.Api.Domain;

namespace Metadata.Api.Infrastructure.Persistence;

/// <summary>
/// Résultat d'une recherche, mis en cache pour <strong>tous</strong> les
/// ménages : la notice d'un ISBN est universelle, et ce cache n'est donc
/// volontairement pas partitionné. Il ne contient rien qui soit propre à un
/// ménage — ni qui a cherché, ni ce qu'il possède.
/// <para>
/// Un code inconnu de toutes les sources est mis en cache lui aussi
/// (<see cref="Record"/> à <c>null</c>), pour ne pas réinterroger les
/// sources à chaque scan. Une source <em>en panne</em>, elle, ne l'est
/// jamais : une panne ne dit rien du code.
/// </para>
/// </summary>
public sealed class CachedLookup
{
    private CachedLookup()
    {
        // Réservé à EF Core.
        Code = string.Empty;
    }

    /// <summary>Code normalisé (<see cref="ProductCode.Value"/>), clé primaire.</summary>
    public string Code { get; private set; }

    /// <summary>Notice trouvée, ou <c>null</c> si aucune source ne connaît le code.</summary>
    public MetadataRecord? Record { get; private set; }

    public DateTimeOffset FetchedAt { get; private set; }

    public static CachedLookup Create(ProductCode code, MetadataRecord? record, DateTimeOffset fetchedAt) =>
        new() { Code = code.Value, Record = record, FetchedAt = fetchedAt };

    public void Refresh(MetadataRecord? record, DateTimeOffset fetchedAt)
    {
        Record = record;
        FetchedAt = fetchedAt;
    }

    public bool IsFresh(DateTimeOffset now, TimeSpan foundTtl, TimeSpan notFoundTtl) =>
        now - FetchedAt < (Record is null ? notFoundTtl : foundTtl);
}
