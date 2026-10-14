using System.Text.Json;
using System.Text.Json.Serialization;
using Catalogue.Api.Infrastructure.Persistence;
using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Catalogue.Api.Infrastructure;

public static class CatalogueApiRegistration
{
    public static IServiceCollection AddCatalogueApi(this IServiceCollection services, IConfiguration configuration)
    {
        var keycloak = configuration.GetSection(KeycloakOptions.Section).Get<KeycloakOptions>()
            ?? throw new InvalidOperationException("Section de configuration « Keycloak » absente.");

        if (string.IsNullOrWhiteSpace(keycloak.Authority) || string.IsNullOrWhiteSpace(keycloak.Audience))
        {
            throw new InvalidOperationException("« Keycloak:Authority » et « Keycloak:Audience » sont requis.");
        }

        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.Section));

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.Converters.Add(new MediaSpecificsJsonConverter());
        });

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, ClaimsTenantProvider>();

        // Résoudre un TenantId est la seule façon d'obtenir un DbContext :
        // aucun contexte ne peut donc être construit sans ménage résolu.
        services.AddScoped(serviceProvider => serviceProvider.GetRequiredService<ITenantProvider>().Current);

        services.AddTransient<IClaimsTransformation, KeycloakRolesClaimsTransformation>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = keycloak.Authority;
                options.Audience = keycloak.Audience;
                options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
                options.MapInboundClaims = false;

                // Issuer, audience, signature et durée de vie : aucune de ces
                // validations ne doit être désactivée, même en développement.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = keycloak.Authority,
                    ValidateAudience = true,
                    ValidAudience = keycloak.Audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = "roles"
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.HouseholdMember, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ClaimsTenantProvider.ClaimName));

            options.AddPolicy(Policies.CatalogueWrite, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ClaimsTenantProvider.ClaimName)
                .RequireRole(Roles.Adult, Roles.HouseholdAdmin));

            // Rien n'est accessible sans ménage, sauf ce qui est explicitement
            // marqué anonyme.
            options.FallbackPolicy = options.GetPolicy(Policies.HouseholdMember);
        });

        services.AddDbContext<CatalogueDbContext>((serviceProvider, options) =>
        {
            var tenant = serviceProvider.GetRequiredService<TenantId>();

            var connectionString = configuration.GetConnectionString("Catalogue")
                ?? throw new InvalidOperationException("Chaîne de connexion « Catalogue » absente.");

            options
                .UseNpgsql(connectionString, npgsql => npgsql
                    .MigrationsHistoryTable(CatalogueDbContext.MigrationsHistoryTable, tenant.Value))
                .UseSnakeCaseNamingConvention()
                .ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
        });

        services.AddProblemDetails();

        return services;
    }

    public static WebApplication UseCatalogueApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();

        return app;
    }
}
