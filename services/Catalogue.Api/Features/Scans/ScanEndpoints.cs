using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Metadata;
using Catalogue.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Catalogue.Api.Features.Scans;

/// <summary>
/// Chemin « scan » : le frontend envoie le code lu, Catalogue.Api regarde
/// si le ménage possède déjà le média, sinon interroge Metadata.Api et
/// renvoie une fiche pré-remplie. Rien n'est enregistré ici : l'utilisateur
/// valide ou corrige, puis enregistre par le chemin de saisie ordinaire.
/// </summary>
public static class ScanEndpoints
{
    public static RouteGroupBuilder MapScanEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/scans")
            .RequireAuthorization(Policies.HouseholdMember)
            .WithTags("Scans");

        group.MapGet("/{code}", ScanAsync);

        return group;
    }

    private static async Task<Results<Ok<ScanResponse>, ValidationProblem>> ScanAsync(
        string code,
        CatalogueDbContext db,
        IMetadataLookup metadata,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var normalized = IsbnFormat.Normalize(code);
        if (normalized is not { Length: 8 or 10 or 12 or 13 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["Le code doit être un ISBN ou un code-barres EAN-8, UPC-A ou EAN-13."]
            });
        }

        // Le doublon se cherche dans le schéma du ménage du jeton, et
        // nulle part ailleurs : un code présent chez un autre ménage n'est
        // pas un doublon, il n'existe pas. Un ISBN se cherche parmi les
        // ISBN, tout autre code parmi les codes-barres des disques.
        var existing = IsbnFormat.IsIsbn(normalized)
            ? await MediaEndpoints.FindByIsbnAsync(db, normalized, cancellationToken)
            : await MediaEndpoints.FindByBarcodeAsync(db, normalized, cancellationToken);

        if (existing is not null)
        {
            return TypedResults.Ok(new ScanResponse(
                normalized,
                new MediaSummaryResponse(
                    existing.Id, existing.Type, existing.Title, existing.Creator, existing.Year,
                    existing.Location, existing.IsLent, existing.AddedAt),
                Draft: null,
                EnrichmentStatus.NotAttempted));
        }

        var lookup = await metadata.LookupAsync(normalized, cancellationToken);

        if (lookup is { Outcome: MetadataLookupOutcome.Found, Record: { } record }
            && ScanDraftMapper.FromRecord(record, normalized, clock.GetUtcNow()) is { } draft)
        {
            return TypedResults.Ok(new ScanResponse(
                normalized, Existing: null, draft, EnrichmentStatus.Found, record.Source, record.Provisional));
        }

        var status = lookup.Outcome == MetadataLookupOutcome.Unavailable
            ? EnrichmentStatus.Unavailable
            : EnrichmentStatus.NotFound;

        return TypedResults.Ok(new ScanResponse(
            normalized, Existing: null, ScanDraftMapper.FromCodeOnly(normalized), status));
    }
}
