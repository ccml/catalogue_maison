using Catalogue.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Catalogue.Api.Infrastructure.Tenancy;

/// <summary>
/// Le schéma étant porté par le modèle (<c>HasDefaultSchema</c>), le modèle
/// diffère d'un ménage à l'autre : la clé de cache doit inclure le ménage,
/// sinon le premier ménage servi imposerait son schéma à tous les suivants.
/// </summary>
public sealed class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is CatalogueDbContext catalogue
            ? (context.GetType(), catalogue.Tenant.Value, designTime)
            : (context.GetType(), designTime);
}
