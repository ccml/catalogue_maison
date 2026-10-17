using Metadata.Api.Domain;
using Metadata.Api.Infrastructure;
using Metadata.Api.Infrastructure.Sources;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Metadata.Api.Features.Films;

/// <summary>
/// Recherche de films par titre. OMDb ne connaît pas les codes-barres :
/// pour un film, le chemin d'entrée est le titre, pas le scan.
/// </summary>
public static class FilmEndpoints
{
    public static RouteGroupBuilder MapFilmEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/films")
            .RequireAuthorization(Policies.MetadataRead)
            .WithTags("Films");

        group.MapGet("/availability", (FilmService films) => TypedResults.Ok(new FilmAvailabilityResponse(films.Enabled)));
        group.MapGet("/", SearchAsync);
        group.MapGet("/{id}", GetAsync);

        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<FilmCandidate>>, ValidationProblem, ProblemHttpResult>> SearchAsync(
        FilmService films,
        CancellationToken cancellationToken,
        string? title = null)
    {
        if (!films.Enabled)
        {
            return Disabled();
        }

        var clean = title?.Trim();
        if (clean is not { Length: >= 2 and <= 200 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Le titre recherché doit comporter de 2 à 200 caractères."]
            });
        }

        try
        {
            return TypedResults.Ok(await films.SearchAsync(clean, cancellationToken));
        }
        catch (MetadataSourceException)
        {
            return Unavailable();
        }
    }

    private static async Task<Results<Ok<MetadataRecord>, NotFound, ValidationProblem, ProblemHttpResult>> GetAsync(
        string id,
        FilmService films,
        CancellationToken cancellationToken)
    {
        if (!films.Enabled)
        {
            return Disabled();
        }

        if (!FilmId.TryParse(id, out var filmId) || filmId is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["id"] = ["L'identifiant doit être un identifiant IMDb (tt suivi de 7 à 10 chiffres)."]
            });
        }

        try
        {
            return await films.GetAsync(filmId, cancellationToken) is { } record
                ? TypedResults.Ok(record)
                : TypedResults.NotFound();
        }
        catch (MetadataSourceException)
        {
            return Unavailable();
        }
    }

    /// <summary>501 : la fonction n'est pas mise en service sur ce déploiement, faute de clé.</summary>
    private static ProblemHttpResult Disabled() => TypedResults.Problem(
        title: "Recherche de films désactivée",
        detail: "Aucune clé OMDb n'est configurée.",
        statusCode: StatusCodes.Status501NotImplemented);

    private static ProblemHttpResult Unavailable() => TypedResults.Problem(
        title: "Source indisponible",
        detail: "OMDb n'a pas pu répondre.",
        statusCode: StatusCodes.Status503ServiceUnavailable);
}

public sealed record FilmAvailabilityResponse(bool Enabled);
