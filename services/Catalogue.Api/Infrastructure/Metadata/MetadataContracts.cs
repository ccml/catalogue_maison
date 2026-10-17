namespace Catalogue.Api.Infrastructure.Metadata;

/// <summary>
/// Contrat public de Metadata.Api (<c>GET /lookups/{code}</c>), transcrit
/// ici plutôt que partagé par un assembly commun : chaque service ne dépend
/// que de la forme HTTP de l'autre.
/// </summary>
public sealed record MetadataLookupResponse(string Code, string Kind, MetadataRecordDto Record);

/// <summary>Notice telle que Metadata.Api la décrit, dans son vocabulaire.</summary>
public sealed record MetadataRecordDto
{
    /// <summary><c>Book</c>, <c>ComicBook</c> ou <c>Music</c>.</summary>
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<string> Creators { get; init; } = [];
    public int? Year { get; init; }
    public string? Publisher { get; init; }
    public int? Pages { get; init; }
    public string? Isbn { get; init; }
    public string? Series { get; init; }
    public int? Volume { get; init; }
    public string? Illustrator { get; init; }
    public string? Label { get; init; }
    public string? Format { get; init; }
    public IReadOnlyList<string> Tracks { get; init; } = [];
    public string Source { get; init; } = string.Empty;
}

public enum MetadataLookupOutcome
{
    Found,
    NotFound,

    /// <summary>Metadata.Api injoignable, en erreur, ou ses sources en panne.</summary>
    Unavailable
}

public sealed record MetadataLookup(MetadataLookupOutcome Outcome, MetadataRecordDto? Record = null)
{
    public static MetadataLookup NotFound { get; } = new(MetadataLookupOutcome.NotFound);

    public static MetadataLookup Unavailable { get; } = new(MetadataLookupOutcome.Unavailable);
}

/// <summary>Résolution d'un code en notice. Ne lève jamais : un échec est un résultat.</summary>
public interface IMetadataLookup
{
    Task<MetadataLookup> LookupAsync(string code, CancellationToken cancellationToken);
}
