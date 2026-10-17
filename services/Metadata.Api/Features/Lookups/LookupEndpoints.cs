using Metadata.Api.Domain;
using Metadata.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Metadata.Api.Features.Lookups;

/// <summary>
/// Contrat public de Metadata.Api. Il ne connaît pas le catalogue et n'y
/// écrit jamais : Catalogue.Api l'appelle et décide de ce qu'il conserve.
/// </summary>
public static class LookupEndpoints
{
    public static RouteGroupBuilder MapLookupEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/lookups")
            .RequireAuthorization(Policies.MetadataRead)
            .WithTags("Lookups");

        group.MapGet("/{code}", LookupAsync);

        return group;
    }

    /// <summary>
    /// 200 avec la notice ; 404 si aucune source ne connaît le code ; 503 si
    /// on ne peut pas le savoir, une source étant en panne. 503 et non 502 :
    /// la demande est légitime et peut être rejouée plus tard.
    /// </summary>
    private static async Task<Results<Ok<LookupResponse>, NotFound, ValidationProblem, ProblemHttpResult>> LookupAsync(
        string code,
        LookupService lookups,
        CancellationToken cancellationToken)
    {
        if (!ProductCode.TryParse(code, out var productCode) || productCode is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["Le code doit être un ISBN (10 ou 13 caractères) ou un code-barres EAN-8, UPC-A ou EAN-13."]
            });
        }

        var result = await lookups.LookupAsync(productCode, cancellationToken);

        return result.Outcome switch
        {
            LookupOutcome.Found => TypedResults.Ok(new LookupResponse(productCode.Value, productCode.Kind, result.Record!)),
            LookupOutcome.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Problem(
                title: "Sources indisponibles",
                detail: "Aucune notice n'a pu être obtenue : au moins une source externe ne répond pas.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }
}

public sealed record LookupResponse(string Code, ProductCodeKind Kind, MetadataRecord Record);
