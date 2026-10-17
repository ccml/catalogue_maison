using Catalogue.Api.Features.Scans;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Metadata;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Catalogue.Api.Features.Films;

/// <summary>
/// Recherche d'un film par son titre, pour pré-remplir la fiche. C'est le
/// chemin d'entrée des films : leur source (OMDb) ne connaît pas les
/// codes-barres. Comme le scan, rien n'est enregistré ici.
/// <para>
/// La fonction n'existe que si Metadata.Api dispose d'une clé OMDb ; le
/// frontend le demande à <c>/films/availability</c> avant de la proposer.
/// </para>
/// </summary>
public static class FilmEndpoints
{
    public static RouteGroupBuilder MapFilmEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/films")
            .RequireAuthorization(Policies.HouseholdMember)
            .WithTags("Films");

        group.MapGet("/availability", AvailabilityAsync);
        group.MapGet("/", SearchAsync);
        group.MapGet("/{id}", GetAsync);

        return group;
    }

    private static async Task<Ok<FilmAvailabilityResponse>> AvailabilityAsync(
        IMetadataLookup metadata,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(new FilmAvailabilityResponse(await metadata.FilmSearchEnabledAsync(cancellationToken)));

    private static async Task<Results<Ok<FilmSearchResponse>, ValidationProblem>> SearchAsync(
        IMetadataLookup metadata,
        CancellationToken cancellationToken,
        string? title = null)
    {
        var clean = title?.Trim();
        if (clean is not { Length: >= 2 and <= 200 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Le titre recherché doit comporter de 2 à 200 caractères."]
            });
        }

        var search = await metadata.SearchFilmsAsync(clean, cancellationToken);

        return TypedResults.Ok(new FilmSearchResponse(
            search.Films.Select(f => new FilmCandidateResponse(f.Id, f.Title, f.Year)).ToList(),
            search.Outcome == MetadataLookupOutcome.Unavailable));
    }

    private static async Task<Results<Ok<FilmDraftResponse>, NotFound, ProblemHttpResult>> GetAsync(
        string id,
        IMetadataLookup metadata,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var lookup = await metadata.GetFilmAsync(id, cancellationToken);

        return lookup switch
        {
            { Outcome: MetadataLookupOutcome.Found, Record: { } record }
                when ScanDraftMapper.FromRecord(record, code: string.Empty, clock.GetUtcNow()) is { } draft
                => TypedResults.Ok(new FilmDraftResponse(draft, record.Source)),
            { Outcome: MetadataLookupOutcome.Unavailable } => TypedResults.Problem(
                title: "Fiche indisponible",
                detail: "La recherche de films ne répond pas pour l'instant.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => TypedResults.NotFound()
        };
    }
}

public sealed record FilmAvailabilityResponse(bool Enabled);

public sealed record FilmCandidateResponse(string Id, string Title, string? Year);

/// <param name="Films">Films proposés, à départager par l'utilisateur.</param>
/// <param name="Unavailable">Vrai si la recherche n'a pas pu se faire : liste vide, mais pas « aucun film ».</param>
public sealed record FilmSearchResponse(IReadOnlyList<FilmCandidateResponse> Films, bool Unavailable);

/// <summary>Fiche pré-remplie d'un film, à valider avant enregistrement.</summary>
public sealed record FilmDraftResponse(ScanDraft Draft, string Source);
