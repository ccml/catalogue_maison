using Metadata.Api.Domain;

namespace Metadata.Api.Infrastructure.Persistence;

/// <summary>
/// Fiche d'un film, mise en cache par identifiant IMDb pour tous les
/// ménages. OMDb limite le nombre d'appels quotidiens : une fiche lue une
/// fois n'est plus redemandée avant son délai.
/// <para>
/// Les <em>recherches</em> par titre, elles, ne sont pas mises en cache :
/// elles varient avec chaque saisie, et le cache se remplirait de fragments
/// de titres sans jamais resservir.
/// </para>
/// </summary>
public sealed class CachedFilm
{
    private CachedFilm()
    {
        // Réservé à EF Core.
        ImdbId = string.Empty;
        Record = null!;
    }

    public string ImdbId { get; private set; }

    public MetadataRecord Record { get; private set; }

    public DateTimeOffset FetchedAt { get; private set; }

    public int RecordVersion { get; private set; }

    public static CachedFilm Create(FilmId id, MetadataRecord record, DateTimeOffset fetchedAt) =>
        new() { ImdbId = id.Value, Record = record, FetchedAt = fetchedAt, RecordVersion = CachedLookup.CurrentRecordVersion };

    public void Refresh(MetadataRecord record, DateTimeOffset fetchedAt)
    {
        Record = record;
        FetchedAt = fetchedAt;
        RecordVersion = CachedLookup.CurrentRecordVersion;
    }

    public bool IsFresh(DateTimeOffset now, TimeSpan ttl) =>
        RecordVersion == CachedLookup.CurrentRecordVersion && now - FetchedAt < ttl;
}
