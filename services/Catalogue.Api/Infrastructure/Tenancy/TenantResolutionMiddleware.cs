namespace Catalogue.Api.Infrastructure.Tenancy;

/// <summary>
/// Rejette en 401 toute requête authentifiée dont le jeton ne porte pas de
/// ménage exploitable — claim absent, ou valeur qui ne passe pas
/// <see cref="TenantId"/>. Placé juste après l'authentification, afin que la
/// réponse soit un 401 franc plutôt qu'une erreur au moment d'ouvrir la base.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider)
    {
        if (context.User.Identity?.IsAuthenticated == true && !tenantProvider.TryGetCurrent(out _))
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
}
