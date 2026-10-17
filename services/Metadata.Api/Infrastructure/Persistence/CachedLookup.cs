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
    /// <summary>
    /// Version de la forme des notices. À incrémenter quand une source
    /// apprend à lire un champ de plus : les notices écrites avant sont alors
    /// tenues pour périmées et redemandées, au lieu de servir des mois durant
    /// une notice amputée de ce champ.
    /// <list type="bullet">
    ///   <item>1 — notice initiale.</item>
    ///   <item>2 — série et tome d'un livre, collection et numéro, genre.</item>
    ///   <item>3 — BD reconnue par la forme du contenu ; mention d'édition.</item>
    ///   <item>4 — notice provisoire signalée ; fiches de films (durée, acteurs).</item>
    /// </list>
    /// </summary>
    public const int CurrentRecordVersion = 4;

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

    /// <summary>Version de la forme de la notice à son écriture (<see cref="CurrentRecordVersion"/>).</summary>
    public int RecordVersion { get; private set; }

    public static CachedLookup Create(ProductCode code, MetadataRecord? record, DateTimeOffset fetchedAt) =>
        new() { Code = code.Value, Record = record, FetchedAt = fetchedAt, RecordVersion = CurrentRecordVersion };

    public void Refresh(MetadataRecord? record, DateTimeOffset fetchedAt)
    {
        Record = record;
        FetchedAt = fetchedAt;
        RecordVersion = CurrentRecordVersion;
    }

    /// <summary>
    /// Vrai si la notice peut être servie sans redemander les sources : dans
    /// son délai, et écrite sous la forme actuelle.
    /// </summary>
    public bool IsFresh(DateTimeOffset now, TimeSpan foundTtl, TimeSpan notFoundTtl, TimeSpan provisionalTtl) =>
        RecordVersion == CurrentRecordVersion
        && now - FetchedAt < Record switch
        {
            null => notFoundTtl,
            { Provisional: true } => provisionalTtl,
            _ => foundTtl
        };
}
