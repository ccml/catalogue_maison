using Metadata.Api.Domain;
using Metadata.Api.Infrastructure.Persistence;
using Metadata.Api.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Metadata.Api.Features.Films;

/// <summary>
/// Recherche de films par titre, et lecture de leur fiche. Désactivée tant
/// qu'aucune clé OMDb n'est configurée.
/// </summary>
public sealed class FilmService(
    OmdbClient omdb,
    MetadataDbContext db,
    IOptions<EnrichmentOptions> options,
    TimeProvider clock,
    ILogger<FilmService> logger)
{
    public bool Enabled => options.Value.FilmSearchEnabled;

    /// <exception cref="MetadataSourceException">Si OMDb n'a pas pu répondre.</exception>
    public Task<IReadOnlyList<FilmCandidate>> SearchAsync(string title, CancellationToken cancellationToken) =>
        omdb.SearchAsync(title, cancellationToken);

    /// <summary>Fiche du film, depuis le cache ou OMDb ; <c>null</c> si inconnu.</summary>
    /// <exception cref="MetadataSourceException">Si OMDb n'a pas pu répondre et qu'aucune fiche n'est en cache.</exception>
    public async Task<MetadataRecord?> GetAsync(FilmId id, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var cached = await db.Films.FirstOrDefaultAsync(f => f.ImdbId == id.Value, cancellationToken);

        if (cached is not null && cached.IsFresh(now, TimeSpan.FromDays(options.Value.FoundTtlDays)))
        {
            return cached.Record;
        }

        MetadataRecord? record;
        try
        {
            record = await omdb.GetAsync(id, cancellationToken);
        }
        catch (MetadataSourceException) when (cached is not null)
        {
            // Une fiche périmée vaut mieux que rien quand OMDb ne répond pas.
            return cached.Record;
        }

        if (record is null)
        {
            return null;
        }

        if (cached is null)
        {
            db.Films.Add(CachedFilm.Create(id, record, now));
        }
        else
        {
            cached.Refresh(record, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogDebug(exception, "Fiche de {Film} déjà mise en cache par une requête concurrente.", id.Value);
        }

        return record;
    }
}
