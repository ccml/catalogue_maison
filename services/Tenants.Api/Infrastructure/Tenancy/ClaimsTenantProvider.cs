namespace Tenants.Api.Infrastructure.Tenancy;

/// <summary>
/// Lit le ménage dans le claim <c>tenant</c> du jeton, et
/// <strong>nulle part ailleurs</strong> : jamais depuis l'URL, le corps, un
/// paramètre de query ou un en-tête — un client ne doit pas pouvoir désigner
/// le ménage sur lequel il travaille.
/// </summary>
public sealed class ClaimsTenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider
{
    public const string ClaimName = "tenant";

    public TenantId Current =>
        TryGetCurrent(out var tenant) && tenant is not null
            ? tenant
            : throw new TenantUnresolvedException();

    public bool TryGetCurrent(out TenantId? tenant)
    {
        var raw = httpContextAccessor.HttpContext?.User.FindFirst(ClaimName)?.Value;
        return TenantId.TryCreate(raw, out tenant);
    }
}
