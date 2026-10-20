namespace Catalogue.Api.Infrastructure.Tenancy;

/// <summary>
/// Rejette en 401 toute requête authentifiée dont le jeton ne porte pas de
/// ménage exploitable — claim absent, ou valeur qui ne passe pas
/// <see cref="TenantId"/>. Placé juste après l'authentification, afin que la
/// réponse soit un 401 franc plutôt qu'une erreur au moment d'ouvrir la base.
/// <para>
/// Seule exception : un endpoint marqué <see cref="ServiceEndpoint"/>,
/// appelé par un jeton qui porte <em>le</em> rôle de service propre à cet
/// endpoint et <em>aucun</em> claim <c>tenant</c> — pas une valeur invalide,
/// une absence.
/// </para>
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && !tenantProvider.TryGetCurrent(out _)
            && !IsServiceOnItsEndpoint(context))
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

    private static bool IsServiceOnItsEndpoint(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ServiceEndpoint>() is { } endpoint
        && context.User.IsInRole(endpoint.Role)
        && !context.User.HasClaim(c => c.Type == ClaimsTenantProvider.ClaimName);
}

/// <summary>
/// Marque un endpoint de service à service : le seul endroit où un jeton
/// sans ménage peut passer, et seulement avec le rôle de service qui lui
/// revient. Le ménage y est désigné par l'URL, par un service qui a vérifié
/// en amont qu'il en avait le droit.
/// </summary>
public sealed class ServiceEndpoint
{
    /// <summary>Création et suppression du schéma d'un ménage.</summary>
    public static readonly ServiceEndpoint Provisioning = new(Roles.Provisioning);

    /// <summary>Lecture d'un catalogue partagé, demandes de prêt.</summary>
    public static readonly ServiceEndpoint Sharing = new(Roles.Sharing);

    private ServiceEndpoint(string role) => Role = role;

    public string Role { get; }
}
