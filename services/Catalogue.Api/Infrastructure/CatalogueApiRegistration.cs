using System.Text.Json;
using System.Text.Json.Serialization;
using Catalogue.Api.Infrastructure.Metadata;
using Catalogue.Api.Infrastructure.Persistence;
using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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
                if (!string.IsNullOrWhiteSpace(keycloak.InternalAuthority))
                {
                    // Découverte et clés par le réseau interne ; l'issuer
                    // attendu, plus bas, reste l'URL publique.
                    options.MetadataAddress =
                        $"{keycloak.InternalAuthority.TrimEnd('/')}/.well-known/openid-configuration";
                }
                options.Audience = keycloak.Audience;
                options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
                options.MapInboundClaims = false;

                // Issuer, audience, signature et durée de vie : aucune de ces
                // validations ne doit être désactivée, même en développement.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = keycloak.Authority,
                    ValidIssuers = keycloak.AdditionalIssuers,
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

            options.AddPolicy(Policies.Provisioning, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Provisioning)
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == ClaimsTenantProvider.ClaimName)));

            options.AddPolicy(Policies.Sharing, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Sharing)
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == ClaimsTenantProvider.ClaimName)));

            // Rien n'est accessible sans ménage, sauf ce qui est explicitement
            // marqué anonyme.
            options.FallbackPolicy = options.GetPolicy(Policies.HouseholdMember);
        });

        services.AddSingleton<TenantConnectionStringFactory>();
        services.AddSingleton<SchemaVersionRegistry>();
        services.AddScoped<HouseholdSchemaProvisioner>();
        services.AddSingleton<HouseholdContextFactory>();

        services.AddDbContext<CatalogueDbContext>((serviceProvider, options) =>
        {
            var tenant = serviceProvider.GetRequiredService<TenantId>();
            var connectionStrings = serviceProvider.GetRequiredService<TenantConnectionStringFactory>();

            options
                .UseNpgsql(connectionStrings.For(tenant), npgsql => npgsql
                    .MigrationsHistoryTable(CatalogueDbContext.MigrationsHistoryTable))
                .UseSnakeCaseNamingConvention();
        });

        // Le frontend est servi depuis une autre origine. La liste est
        // configurée, et vide par défaut : aucune origine n'est autorisée
        // tant qu'elle n'a pas été déclarée.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(allowedOrigins)
            .WithHeaders("Authorization", "Content-Type")
            .WithMethods("GET", "POST", "PUT", "DELETE")));

        AddMetadataClient(services, configuration);

        services.AddProblemDetails();

        return services;
    }

    /// <summary>
    /// Appel à Metadata.Api en service à service : jeton client credentials
    /// propre à ce service, jamais celui de l'utilisateur.
    /// </summary>
    private static void AddMetadataClient(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MetadataOptions>(configuration.GetSection(MetadataOptions.Section));

        var options = configuration.GetSection(MetadataOptions.Section).Get<MetadataOptions>() ?? new MetadataOptions();
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        services.AddHttpClient(ServiceTokenProvider.HttpClientName, http => http.Timeout = timeout);
        services.AddSingleton<ServiceTokenProvider>();
        services.AddTransient<ServiceTokenHandler>();

        services.AddHttpClient<IMetadataLookup, MetadataClient>(http =>
            {
                // Sans adresse configurée, l'enrichissement est simplement
                // indisponible : le scan reste utilisable en saisie.
                http.BaseAddress = options.BaseUrl ?? new Uri("http://metadata-api.non-configure.invalid/");
                http.Timeout = timeout;
            })
            .AddHttpMessageHandler<ServiceTokenHandler>();
    }

    public static WebApplication UseCatalogueApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseMiddleware<SchemaVersionMiddleware>();
        app.UseAuthorization();

        return app;
    }
}
