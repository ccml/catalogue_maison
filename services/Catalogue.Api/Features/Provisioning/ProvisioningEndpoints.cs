using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Persistence;
using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Catalogue.Api.Features.Provisioning;

/// <param name="Existed">Faux quand le schéma était déjà absent : la suppression est idempotente.</param>
public sealed record SchemaDroppedResponse(bool Existed);

/// <param name="AppliedMigrations">Migrations appliquées par cet appel ; 0 quand le schéma était déjà à jour.</param>
public sealed record SchemaProvisionedResponse(int AppliedMigrations);

/// <summary>
/// Provisionnement du schéma d'un ménage — création et suppression —,
/// appelé par Tenants.Api seul, en client credentials. Ce service ne décide
/// pas qu'un ménage existe : Tenants.Api tient le registre et pilote.
/// <para>
/// C'est le seul endpoint où le ménage est désigné par l'URL. Il n'est pas
/// atteignable par un jeton de personne : le rôle de provisionnement n'est
/// porté que par le compte de service de Tenants.Api, et un jeton qui porte
/// un claim <c>tenant</c> est refusé.
/// </para>
/// </summary>
public static class ProvisioningEndpoints
{
    public static RouteGroupBuilder MapProvisioningEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/admin/households")
            .RequireAuthorization(Policies.Provisioning)
            .WithMetadata(ProvisioningEndpoint.Instance)
            .WithTags("Provisioning");

        group.MapPut("/{id}/schema", ProvisionAsync);
        group.MapDelete("/{id}/schema", DropAsync);

        return group;
    }

    private static async Task<Results<Ok<SchemaProvisionedResponse>, ValidationProblem>> ProvisionAsync(
        string id,
        HouseholdSchemaProvisioner provisioner,
        CancellationToken cancellationToken)
    {
        if (!TenantId.TryCreate(id, out var tenant) || tenant is null || tenant.Value != id)
        {
            return InvalidId();
        }

        var applied = await provisioner.ProvisionAsync(tenant, cancellationToken);
        return TypedResults.Ok(new SchemaProvisionedResponse(applied));
    }

    private static async Task<Results<Ok<SchemaDroppedResponse>, ValidationProblem>> DropAsync(
        string id,
        HouseholdSchemaProvisioner provisioner,
        CancellationToken cancellationToken)
    {
        if (!TenantId.TryCreate(id, out var tenant) || tenant is null || tenant.Value != id)
        {
            return InvalidId();
        }

        return TypedResults.Ok(new SchemaDroppedResponse(await provisioner.DropAsync(tenant, cancellationToken)));
    }

    private static ValidationProblem InvalidId() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["id"] = ["Identifiant de ménage invalide."]
        });
}
