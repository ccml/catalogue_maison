using System.Collections.Concurrent;
using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Catalogue.Api.Infrastructure.Persistence;

/// <summary>
/// Retient les ménages dont le schéma est à la version attendue.
/// <para>
/// Seuls les contrôles <em>réussis</em> sont mémorisés : un ménage en retard
/// est revérifié à chaque requête, de sorte qu'appliquer la migration le
/// remette en service sans redémarrer le processus.
/// </para>
/// </summary>
public sealed class SchemaVersionRegistry
{
    private readonly ConcurrentDictionary<string, bool> _upToDate = new(StringComparer.Ordinal);

    public bool IsKnownUpToDate(TenantId tenant) => _upToDate.ContainsKey(tenant.Value);

    public void MarkUpToDate(TenantId tenant) => _upToDate[tenant.Value] = true;

    /// <summary>Après suppression du schéma : le prochain accès revérifie.</summary>
    public void Forget(TenantId tenant) => _upToDate.TryRemove(tenant.Value, out _);
}

/// <summary>
/// Refuse de servir un ménage dont le schéma n'est pas à la version attendue.
/// <para>
/// Un schéma en retard de migration est un <strong>incident</strong> : mieux
/// vaut un refus franc et lisible qu'un échec au hasard des requêtes, selon
/// qu'elles touchent ou non une colonne manquante. Le refus est un 503, et
/// non un 500 : la demande était légitime, c'est le service qui n'est pas en
/// état de la servir.
/// </para>
/// </summary>
public sealed class SchemaVersionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantProvider tenantProvider,
        SchemaVersionRegistry registry,
        ILogger<SchemaVersionMiddleware> logger)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || !tenantProvider.TryGetCurrent(out var tenant)
            || tenant is null
            || registry.IsKnownUpToDate(tenant))
        {
            await next(context);
            return;
        }

        var db = context.RequestServices.GetRequiredService<CatalogueDbContext>();

        string[] pending;
        try
        {
            pending = [.. await db.Database.GetPendingMigrationsAsync(context.RequestAborted)];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception,
                "Impossible de déterminer la version du schéma du ménage {Tenant}.", tenant.Value);
            await RefuseAsync(context, tenant.Value, "Version du schéma indéterminable.");
            return;
        }

        if (pending.Length > 0)
        {
            logger.LogError(
                "Le schéma du ménage {Tenant} est en retard de {Count} migration(s) : {Migrations}.",
                tenant.Value, pending.Length, string.Join(", ", pending));
            await RefuseAsync(context, tenant.Value, $"{pending.Length} migration(s) non appliquée(s).");
            return;
        }

        registry.MarkUpToDate(tenant);
        await next(context);
    }

    private static async Task RefuseAsync(HttpContext context, string tenant, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/problem+json";

        // Le nom du schéma n'est pas exposé au client.
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://cataloguemaison/errors/schema-en-retard",
            title = "Catalogue indisponible",
            status = StatusCodes.Status503ServiceUnavailable,
            detail = "Le catalogue de ce ménage n'est pas à la version attendue. Contactez l'administration du ménage."
        });

        context.Items["schema-en-retard"] = $"{tenant} : {detail}";
    }
}
