using Metadata.Api.Domain;
using Metadata.Api.Infrastructure.Persistence;
using Metadata.Api.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Metadata.Api.Features.Lookups;

/// <summary>Issue d'une recherche.</summary>
public enum LookupOutcome
{
    Found,

    /// <summary>Toutes les sources concernées ont répondu, aucune ne connaît le code.</summary>
    NotFound,

    /// <summary>
    /// Aucune notice, et au moins une source n'a pas pu répondre : on ne sait
    /// pas si le code est connu. Rien n'est mis en cache.
    /// </summary>
    Unavailable
}

public sealed record LookupResult(LookupOutcome Outcome, MetadataRecord? Record);

/// <summary>
/// Résout un code en notice : le cache d'abord, puis les sources dans leur
/// ordre d'enregistrement, la première qui trouve l'emporte.
/// </summary>
public sealed class LookupService(
    MetadataDbContext db,
    IEnumerable<IMetadataSource> sources,
    IOptions<EnrichmentOptions> options,
    TimeProvider clock,
    ILogger<LookupService> logger)
{
    public async Task<LookupResult> LookupAsync(ProductCode code, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();

        var cached = await db.Lookups.FirstOrDefaultAsync(l => l.Code == code.Value, cancellationToken);
        if (cached is not null && cached.IsFresh(
                now, TimeSpan.FromDays(settings.FoundTtlDays), TimeSpan.FromDays(settings.NotFoundTtlDays)))
        {
            return Result(cached.Record);
        }

        var failures = 0;
        MetadataRecord? record = null;

        foreach (var source in sources.Where(s => s.Supports(code)))
        {
            try
            {
                record = await source.LookupAsync(code, cancellationToken);
            }
            catch (MetadataSourceException exception)
            {
                failures++;
                logger.LogWarning(exception, "Source {Source} indisponible pour {Code}.", source.Name, code.Value);
                continue;
            }

            if (record is not null)
            {
                break;
            }
        }

        if (record is null && failures > 0)
        {
            // Une notice périmée vaut mieux que rien quand les sources sont
            // en panne : on la sert sans la rafraîchir.
            return cached?.Record is { } stale
                ? new LookupResult(LookupOutcome.Found, stale)
                : new LookupResult(LookupOutcome.Unavailable, null);
        }

        if (cached is null)
        {
            db.Lookups.Add(CachedLookup.Create(code, record, now));
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
            // Deux scans simultanés du même code : l'autre a écrit le
            // premier. Le résultat est le même, rien à rattraper.
            logger.LogDebug(exception, "Notice de {Code} déjà mise en cache par une requête concurrente.", code.Value);
        }

        return Result(record);
    }

    private static LookupResult Result(MetadataRecord? record) =>
        record is null
            ? new LookupResult(LookupOutcome.NotFound, null)
            : new LookupResult(LookupOutcome.Found, record);
}
