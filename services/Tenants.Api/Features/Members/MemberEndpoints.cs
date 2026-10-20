using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Tenants.Api.Infrastructure;
using Tenants.Api.Infrastructure.Keycloak;
using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Features.Members;

public static class MemberEndpoints
{
    public static RouteGroupBuilder MapMemberEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/members")
            .RequireAuthorization(Policies.HouseholdAdmin)
            .AddEndpointFilter(TranslateFailuresAsync)
            .WithTags("Members");

        group.MapGet("/", ListAsync);
        group.MapGet("/inconsistencies", InconsistenciesAsync);
        group.MapPost("/{id:guid}/register", RegisterAsync);
        group.MapDelete("/{id:guid}/registry-entry", RemoveOrphanEntryAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}/role", UpdateRoleAsync);
        group.MapPut("/{id:guid}/password", ResetPasswordAsync);
        group.MapPost("/{id:guid}/deactivate", (Guid id, HttpContext http, ITenantProvider tenants, MemberService members, CancellationToken ct) =>
            SetActiveAsync(id, false, http, tenants, members, ct));
        group.MapPost("/{id:guid}/reactivate", (Guid id, HttpContext http, ITenantProvider tenants, MemberService members, CancellationToken ct) =>
            SetActiveAsync(id, true, http, tenants, members, ct));
        group.MapDelete("/{id:guid}", DeleteAsync);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<MemberResponse>>> ListAsync(
        HttpContext http, ITenantProvider tenants, MemberService members, CancellationToken cancellationToken) =>
        TypedResults.Ok(await members.ListAsync(CallerOf(http, tenants), cancellationToken));

    private static async Task<Ok<InconsistenciesResponse>> InconsistenciesAsync(
        HttpContext http, ITenantProvider tenants, MemberService members, CancellationToken cancellationToken) =>
        TypedResults.Ok(await members.InconsistenciesAsync(CallerOf(http, tenants), cancellationToken));

    private static async Task<Ok<MemberResponse>> RegisterAsync(
        Guid id, HttpContext http, ITenantProvider tenants, MemberService members, CancellationToken cancellationToken) =>
        TypedResults.Ok(await members.RegisterAsync(CallerOf(http, tenants), id, cancellationToken));

    private static async Task<NoContent> RemoveOrphanEntryAsync(
        Guid id, HttpContext http, ITenantProvider tenants, MemberService members, CancellationToken cancellationToken)
    {
        await members.RemoveOrphanEntryAsync(CallerOf(http, tenants), id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<MemberResponse>, ValidationProblem>> CreateAsync(
        CreateMemberRequest request,
        HttpContext http,
        ITenantProvider tenants,
        MemberService members,
        CancellationToken cancellationToken)
    {
        if (!MemberValidation.TryValidate(request, out var validated, out var errors) || validated is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var member = await members.CreateAsync(CallerOf(http, tenants), validated, cancellationToken);
        return TypedResults.Created($"/members/{member.Id}", member);
    }

    private static async Task<Results<Ok<MemberResponse>, ValidationProblem>> UpdateRoleAsync(
        Guid id,
        UpdateMemberRoleRequest request,
        HttpContext http,
        ITenantProvider tenants,
        MemberService members,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        MemberValidation.ValidateRole(request.Role, request.IsAdmin, errors);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await members.UpdateRoleAsync(
            CallerOf(http, tenants), id, request.Role!.Value, request.IsAdmin, cancellationToken));
    }

    private static async Task<Results<NoContent, ValidationProblem>> ResetPasswordAsync(
        Guid id,
        ResetPasswordRequest request,
        HttpContext http,
        ITenantProvider tenants,
        MemberService members,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        MemberValidation.ValidatePassword(request.TemporaryPassword, errors);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await members.ResetPasswordAsync(CallerOf(http, tenants), id, request.TemporaryPassword!, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<MemberResponse>> SetActiveAsync(
        Guid id,
        bool active,
        HttpContext http,
        ITenantProvider tenants,
        MemberService members,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await members.SetActiveAsync(CallerOf(http, tenants), id, active, cancellationToken));

    private static async Task<NoContent> DeleteAsync(
        Guid id, HttpContext http, ITenantProvider tenants, MemberService members, CancellationToken cancellationToken)
    {
        await members.DeleteAsync(CallerOf(http, tenants), id, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Le ménage vient du claim <c>tenant</c>, le compte du claim <c>sub</c> :
    /// rien de ce qu'envoie le client ne désigne l'un ou l'autre.
    /// </summary>
    private static Caller CallerOf(HttpContext http, ITenantProvider tenants)
    {
        var subject = http.User.FindFirstValue("sub");
        return Guid.TryParse(subject, out var id)
            ? new Caller(tenants.Current, id)
            : throw new InvalidOperationException("Jeton d'administrateur sans « sub » exploitable.");
    }

    /// <summary>Traduit les refus de l'administration des membres en réponses HTTP. Partagé avec l'ajout d'un administrateur par l'opérateur.</summary>
    internal static async ValueTask<object?> TranslateFailuresAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (MemberNotFoundException exception)
        {
            return Problem(StatusCodes.Status404NotFound, "membre-introuvable", "Membre introuvable", exception.Message);
        }
        catch (MemberConflictException exception)
        {
            return Problem(StatusCodes.Status409Conflict, exception.Code, "Opération refusée", exception.Message);
        }
        catch (UsernameTakenException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "identifiant-pris", "Identifiant déjà utilisé", exception.Message);
        }
        catch (PasswordRejectedException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["temporaryPassword"] = [exception.Message]
            });
        }
        catch (RegistryUnavailableException exception)
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, "registre-indisponible", "Opération incomplète", exception.Message);
        }
        catch (IdentityDirectoryException exception)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(MemberEndpoints))
                .LogError(exception, "Échec d'un appel à l'Admin REST API de Keycloak.");
            return Problem(
                StatusCodes.Status502BadGateway,
                "keycloak-indisponible",
                "Fournisseur d'identité indisponible",
                "Keycloak n'a pas pu traiter la demande. Réessayez dans un instant.");
        }
    }

    internal static ProblemHttpResult Problem(int status, string code, string title, string detail) =>
        TypedResults.Problem(
            statusCode: status,
            type: $"https://cataloguemaison/errors/{code}",
            title: title,
            detail: detail);
}
