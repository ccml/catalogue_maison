using Microsoft.AspNetCore.Http.HttpResults;
using Tenants.Api.Domain;
using Tenants.Api.Features.Members;
using Tenants.Api.Infrastructure;
using Tenants.Api.Infrastructure.Catalogue;
using Tenants.Api.Infrastructure.Persistence;
using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Features.Households;

/// <summary>
/// Un ménage, vu par l'opérateur de la plateforme.
/// <para>
/// C'est le <strong>seul</strong> contrat qui expose l'identifiant du ménage
/// — donc le nom de son schéma — côté client, et il n'est servi qu'à
/// l'opérateur. Les membres ne le voient jamais.
/// </para>
/// </summary>
public sealed record HouseholdResponse(
    string Id,
    string DisplayName,
    DateTimeOffset CreatedAt,
    HouseholdStatus Status,
    int MemberCount,
    DateTimeOffset? ArchivedAt);

/// <param name="Id">Immuable : nom du schéma et valeur du claim <c>tenant</c>.</param>
public sealed record CreateHouseholdRequest(string? Id, string? DisplayName);

/// <param name="ConfirmId">
/// L'identifiant du ménage, ressaisi : la purge est irréversible, elle ne
/// part pas sur un clic ou une URL rejouée par erreur.
/// </param>
public sealed record PurgeHouseholdRequest(string? ConfirmId);

/// <summary>Administrateur d'un ménage, ajouté par l'opérateur. Toujours adulte et administrateur.</summary>
public sealed record CreateHouseholdAdminRequest(
    string? Username,
    string? FirstName,
    string? LastName,
    string? TemporaryPassword);

/// <summary>
/// Administration des ménages eux-mêmes, réservée à l'opérateur : un compte
/// sans ménage, porteur du rôle de plateforme. L'administration courante des
/// membres reste dans <c>/members</c>, côté administrateur du ménage.
/// <para>
/// Ici, et ici seulement, le ménage est désigné par l'URL : l'opérateur
/// n'appartient à aucun, il administre la plateforme.
/// </para>
/// </summary>
public static class HouseholdEndpoints
{
    public const int DisplayNameMaxLength = 120;

    public static RouteGroupBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/households")
            .RequireAuthorization(Policies.PlatformAdmin)
            .WithMetadata(PlatformEndpoint.Instance)
            .AddEndpointFilter(TranslateFailuresAsync)
            .AddEndpointFilter(MemberEndpoints.TranslateFailuresAsync)
            .WithTags("Households");

        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPost("/{id}/admins", AddAdminAsync);
        group.MapPost("/{id}/archive", ArchiveAsync);
        group.MapPost("/{id}/restore", RestoreAsync);
        group.MapPost("/{id}/purge", PurgeAsync);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<HouseholdResponse>>> ListAsync(
        HouseholdService households, CancellationToken cancellationToken) =>
        TypedResults.Ok(await households.ListAsync(cancellationToken));

    private static async Task<Results<Created<HouseholdResponse>, ValidationProblem>> CreateAsync(
        CreateHouseholdRequest request,
        HouseholdService households,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var rawId = request.Id?.Trim() ?? string.Empty;
        if (!TenantId.TryCreate(rawId, out var id) || id is null)
        {
            errors["id"] = [$"1 à {TenantId.MaxLength} caractères : lettres minuscules sans accent, chiffres ou souligné, en commençant par une lettre. « public », « information_schema » et « pg_… » sont réservés."];
        }

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is 0 or > DisplayNameMaxLength)
        {
            errors["displayName"] = [$"Le nom affiché est requis ({DisplayNameMaxLength} caractères au plus)."];
        }

        if (errors.Count > 0 || id is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var household = await households.CreateAsync(new ValidatedCreateHousehold(id, displayName), cancellationToken);
        return TypedResults.Created($"/households/{household.Id}", household);
    }

    private static async Task<Results<Created<MemberResponse>, ValidationProblem, NotFound>> AddAdminAsync(
        string id,
        CreateHouseholdAdminRequest request,
        HouseholdService households,
        CancellationToken cancellationToken)
    {
        if (!TenantId.TryCreate(id, out var household) || household is null)
        {
            return TypedResults.NotFound();
        }

        var member = new CreateMemberRequest(
            request.Username, request.FirstName, request.LastName, MemberRole.Adult, true, request.TemporaryPassword);

        if (!MemberValidation.TryValidate(member, out var validated, out var errors) || validated is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var created = await households.AddAdminAsync(household, validated, cancellationToken);
        return TypedResults.Created($"/households/{household.Value}/admins/{created.Id}", created);
    }

    private static async Task<Results<Ok<HouseholdResponse>, NotFound>> ArchiveAsync(
        string id, HouseholdService households, CancellationToken cancellationToken) =>
        TenantId.TryCreate(id, out var household) && household is not null
            ? TypedResults.Ok(await households.ArchiveAsync(household, cancellationToken))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<HouseholdResponse>, NotFound>> RestoreAsync(
        string id, HouseholdService households, CancellationToken cancellationToken) =>
        TenantId.TryCreate(id, out var household) && household is not null
            ? TypedResults.Ok(await households.RestoreAsync(household, cancellationToken))
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, ValidationProblem, NotFound>> PurgeAsync(
        string id,
        PurgeHouseholdRequest request,
        HouseholdService households,
        CancellationToken cancellationToken)
    {
        if (!TenantId.TryCreate(id, out var household) || household is null)
        {
            return TypedResults.NotFound();
        }

        if (!string.Equals(request.ConfirmId?.Trim(), household.Value, StringComparison.Ordinal))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["confirmId"] = ["Ressaisissez exactement l'identifiant du ménage pour confirmer sa suppression."]
            });
        }

        await households.PurgeAsync(household, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async ValueTask<object?> TranslateFailuresAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (HouseholdNotFoundException exception)
        {
            return MemberEndpoints.Problem(StatusCodes.Status404NotFound, "menage-introuvable", "Ménage introuvable", exception.Message);
        }
        catch (HouseholdConflictException exception)
        {
            return MemberEndpoints.Problem(StatusCodes.Status409Conflict, exception.Code, "Opération refusée", exception.Message);
        }
        catch (CatalogueUnavailableException exception)
        {
            return MemberEndpoints.Problem(StatusCodes.Status502BadGateway, "catalogue-indisponible", "Création incomplète", exception.Message);
        }
    }
}
