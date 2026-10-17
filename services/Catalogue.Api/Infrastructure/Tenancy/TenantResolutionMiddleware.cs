namespace Catalogue.Api.Infrastructure.Tenancy;

/// <summary>
/// Rejette en 401 toute requête authentifiée dont le jeton ne porte pas de
/// ménage exploitable — claim absent, ou valeur qui ne passe pas
/// <see cref="TenantId"/>. Placé juste après l'authentification, afin que la
/// réponse soit un 401 franc plutôt qu'une erreur au moment d'ouvrir la base.
/// <para>
/// Seule exception : un endpoint marqué <see cref="ProvisioningEndpoint"/>,
/// appelé par un jeton qui porte le rôle de provisionnement et
/// <em>aucun</em> claim <c>tenant</c> — pas une valeur invalide, une absence.
/// </para>
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && !tenantProvider.TryGetCurrent(out _)
            && !IsProvisioningServiceOnProvisioningEndpoint(context))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://cataloguemaison/errors/tenant-absent",
                title = "Ménage non résolu",
                status = StatusCodes.Status401Unauthorized,
                detail = "Le jeton ne porte pas de ménage exploitable."
            });
            return;
        }

        await next(context);
    }

    private static bool IsProvisioningServiceOnProvisioningEndpoint(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ProvisioningEndpoint>() is not null
        && context.User.IsInRole(Roles.Provisioning)
        && !context.User.HasClaim(c => c.Type == ClaimsTenantProvider.ClaimName);
}

/// <summary>
/// Marque un endpoint de provisionnement : le seul endroit où un jeton sans
/// ménage peut passer, et seulement avec le rôle de provisionnement.
/// </summary>
public sealed class ProvisioningEndpoint
{
    public static readonly ProvisioningEndpoint Instance = new();

    private ProvisioningEndpoint()
    {
    }
}
