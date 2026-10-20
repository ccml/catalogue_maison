using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Tenants.Api.Features.Members;
using Tenants.Api.Infrastructure;
using Tenants.Api.Infrastructure.Catalogue;
using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Features.Sharing;

/// <summary>
/// Partage de catalogue entre ménages.
/// <list type="bullet">
///   <item>L'administrateur d'un ménage invite, voit et retire les partages de son catalogue.</item>
///   <item>L'administrateur d'un autre ménage accepte l'invitation, ou renonce au partage.</item>
///   <item>
///     Tout membre du ménage invité consulte le catalogue partagé ; adultes
///     et administrateur y demandent des prêts. Ces appels sont relayés à
///     Catalogue.Api, une fois le partage vérifié.
///   </item>
/// </list>
/// </summary>
public static class SharingEndpoints
{
    /// <summary>Politique de limitation des acceptations : un code ne se devine pas à la chaîne.</summary>
    public const string AcceptRateLimit = "sharing-accept";

    public static RouteGroupBuilder MapSharingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/sharing")
            .RequireAuthorization(Policies.HouseholdMember)
            .AddEndpointFilter(TranslateFailuresAsync)
            .WithTags("Sharing");

        var admin = group.MapGroup("").RequireAuthorization(Policies.HouseholdAdmin);
        admin.MapPost("/invitations", InviteAsync);
        admin.MapGet("/invitations", ListInvitationsAsync);
        admin.MapDelete("/invitations/{id:guid}", CancelInvitationAsync);
        admin.MapPost("/invitations/accept", AcceptAsync).RequireRateLimiting(AcceptRateLimit);
        admin.MapGet("/granted", ListGrantedAsync);
        admin.MapDelete("/granted/{shareId:guid}", RevokeAsync);
        admin.MapDelete("/received/{shareId:guid}", RenounceAsync);

        group.MapGet("/received", ListReceivedAsync);
        group.MapGet("/received/{shareId:guid}/medias", ListMediasAsync);
        group.MapGet("/received/{shareId:guid}/medias/{id:guid}", GetMediaAsync);

        var adult = group.MapGroup("/received/{shareId:guid}").RequireAuthorization(Policies.HouseholdAdult);
        adult.MapPost("/medias/{id:guid}/loan-requests", RequestLoanAsync);
        adult.MapGet("/loan-requests", ListLoansAsync);
        adult.MapDelete("/loan-requests/{loanId:guid}", CancelLoanAsync);

        return group;
    }

    private static async Task<Created<CreatedInvitationResponse>> InviteAsync(
        ITenantProvider tenants, SharingService sharing, CancellationToken cancellationToken)
    {
        var invitation = await sharing.InviteAsync(tenants.Current, cancellationToken);
        return TypedResults.Created($"/sharing/invitations/{invitation.Id}", invitation);
    }

    private static async Task<Ok<IReadOnlyList<InvitationResponse>>> ListInvitationsAsync(
        ITenantProvider tenants, SharingService sharing, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sharing.ListInvitationsAsync(tenants.Current, cancellationToken));

    private static async Task<NoContent> CancelInvitationAsync(
        Guid id, ITenantProvider tenants, SharingService sharing, CancellationToken cancellationToken)
    {
        await sharing.CancelInvitationAsync(tenants.Current, id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<ShareResponse>> AcceptAsync(
        AcceptInvitationRequest request, ITenantProvider tenants, SharingService sharing, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sharing.AcceptAsync(tenants.Current, request.Code, cancellationToken));

    private static async Task<Ok<IReadOnlyList<ShareResponse>>> ListGrantedAsync(
        ITenantProvider tenants, SharingService sharing, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sharing.ListGrantedAsync(tenants.Current, cancellationToken));

    private static async Task<NoContent> RevokeAsync(
        Guid shareId, ITenantProvider tenants, SharingService sharing, CancellationToken cancellationToken)
    {
        await sharing.RevokeAsync(tenants.Current, shareId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> RenounceAsync(
        Guid shareId, ITenantProvider tenants, SharingService sharing, CancellationToken cancellationToken)
    {
        await sharing.RenounceAsync(tenants.Current, shareId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<ShareResponse>>> ListReceivedAsync(
        ITenantProvider tenants, SharingService sharing, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sharing.ListReceivedAsync(tenants.Current, cancellationToken));

    /// <summary>
    /// Seuls les paramètres connus sont relayés : rien de ce qu'envoie le
    /// client n'atteint Catalogue.Api sans avoir été nommé ici.
    /// </summary>
    private static async Task<IResult> ListMediasAsync(
        Guid shareId,
        ITenantProvider tenants,
        SharingService sharing,
        ICatalogueSharing catalogue,
        CancellationToken cancellationToken,
        string? type = null,
        string? q = null,
        string? sort = null,
        int? page = null,
        int? size = null)
    {
        var share = await sharing.ResolveReceivedAsync(tenants.Current, shareId, cancellationToken);
        var query = new Dictionary<string, string?>
        {
            ["type"] = type,
            ["q"] = q,
            ["sort"] = sort,
            ["page"] = page?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["size"] = size?.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        return Relay(await catalogue.SendAsync(HttpMethod.Get, share.Owner, share.Id, "medias", query, null, cancellationToken));
    }

    private static async Task<IResult> GetMediaAsync(
        Guid shareId,
        Guid id,
        ITenantProvider tenants,
        SharingService sharing,
        ICatalogueSharing catalogue,
        CancellationToken cancellationToken)
    {
        var share = await sharing.ResolveReceivedAsync(tenants.Current, shareId, cancellationToken);
        return Relay(await catalogue.SendAsync(HttpMethod.Get, share.Owner, share.Id, $"medias/{id}", null, null, cancellationToken));
    }

    /// <summary>
    /// Le libellé du demandeur est composé ici, et non par le client : nom du
    /// ménage, puis prénom de la personne. Le ménage prêteur le verra tel quel.
    /// </summary>
    private static async Task<IResult> RequestLoanAsync(
        Guid shareId,
        Guid id,
        HttpContext http,
        ITenantProvider tenants,
        SharingService sharing,
        ICatalogueSharing catalogue,
        CancellationToken cancellationToken)
    {
        var share = await sharing.ResolveReceivedAsync(tenants.Current, shareId, cancellationToken);
        var body = new { BorrowerName = $"{share.ViewerName} ({PersonOf(http.User)})" };

        return Relay(await catalogue.SendAsync(
            HttpMethod.Post, share.Owner, share.Id, $"medias/{id}/loan-requests", null, body, cancellationToken));
    }

    private static async Task<IResult> ListLoansAsync(
        Guid shareId,
        ITenantProvider tenants,
        SharingService sharing,
        ICatalogueSharing catalogue,
        CancellationToken cancellationToken)
    {
        var share = await sharing.ResolveReceivedAsync(tenants.Current, shareId, cancellationToken);
        return Relay(await catalogue.SendAsync(HttpMethod.Get, share.Owner, share.Id, "loan-requests", null, null, cancellationToken));
    }

    private static async Task<IResult> CancelLoanAsync(
        Guid shareId,
        Guid loanId,
        ITenantProvider tenants,
        SharingService sharing,
        ICatalogueSharing catalogue,
        CancellationToken cancellationToken)
    {
        var share = await sharing.ResolveReceivedAsync(tenants.Current, shareId, cancellationToken);
        return Relay(await catalogue.SendAsync(
            HttpMethod.Delete, share.Owner, share.Id, $"loan-requests/{loanId}", null, null, cancellationToken));
    }

    private static string PersonOf(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue("given_name") ?? user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username");
        return string.IsNullOrWhiteSpace(name) ? "?" : name.Trim();
    }

    /// <summary>
    /// Rend la réponse de Catalogue.Api. Les succès et les conflits passent
    /// tels quels ; un 404 reste un 404 ; tout le reste devient une
    /// indisponibilité du catalogue partagé — jamais un « schéma en retard »,
    /// que le client prendrait pour celui de son propre ménage.
    /// </summary>
    private static IResult Relay(CatalogueReply reply) => reply.StatusCode switch
    {
        >= 200 and < 300 or StatusCodes.Status409Conflict => new RelayedResult(reply),
        StatusCodes.Status404NotFound => MemberEndpoints.Problem(
            StatusCodes.Status404NotFound, "introuvable", "Introuvable", "Ce média n'est pas dans le catalogue partagé."),
        _ => Unavailable()
    };

    private static ProblemHttpResult Unavailable() => MemberEndpoints.Problem(
        StatusCodes.Status503ServiceUnavailable,
        "catalogue-partage-indisponible",
        "Catalogue indisponible",
        "Le catalogue de ce ménage est momentanément indisponible. Réessayez dans un instant.");

    private sealed class RelayedResult(CatalogueReply reply) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = reply.StatusCode;
            if (reply.Body.Length > 0)
            {
                httpContext.Response.ContentType = reply.ContentType ?? "application/json";
                await httpContext.Response.Body.WriteAsync(reply.Body, httpContext.RequestAborted);
            }
        }
    }

    private static async ValueTask<object?> TranslateFailuresAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ShareNotFoundException exception)
        {
            return MemberEndpoints.Problem(StatusCodes.Status404NotFound, "partage-introuvable", "Partage introuvable", exception.Message);
        }
        catch (InvitationRejectedException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["code"] = [exception.Message] });
        }
        catch (SharingConflictException exception)
        {
            return MemberEndpoints.Problem(StatusCodes.Status409Conflict, exception.Code, "Opération refusée", exception.Message);
        }
        catch (CatalogueUnavailableException)
        {
            return Unavailable();
        }
    }
}
