using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Tenants.Api.Domain;
using Tenants.Api.Features.Households;
using Tenants.Api.Infrastructure.Catalogue;
using Tenants.Api.Features.Members;
using Tenants.Api.Infrastructure.Keycloak;
using Tenants.Api.Infrastructure.Persistence;
using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Infrastructure;

public static class TenantsApiRegistration
{
    public const string ConnectionName = "Tenants";

    public static IServiceCollection AddTenantsApi(this IServiceCollection services, IConfiguration configuration)
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
        });

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, ClaimsTenantProvider>();
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

            options.AddPolicy(Policies.HouseholdAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ClaimsTenantProvider.ClaimName)
                .RequireRole(Roles.HouseholdAdmin));

            options.AddPolicy(Policies.PlatformAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.PlatformAdmin)
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == ClaimsTenantProvider.ClaimName)));

            // Rien n'est accessible sans ménage, sauf ce qui est explicitement
            // marqué anonyme.
            options.FallbackPolicy = options.GetPolicy(Policies.HouseholdMember);
        });

        services.AddDbContext<TenantsDbContext>(options => options
            .UseNpgsql(
                configuration.GetConnectionString(ConnectionName)
                    ?? throw new InvalidOperationException(
                        $"Chaîne de connexion « {ConnectionName} » absente de la configuration."),
                npgsql => npgsql.MigrationsHistoryTable(TenantsDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention());

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(allowedOrigins)
            .WithHeaders("Authorization", "Content-Type")
            .WithMethods("GET", "POST", "PUT", "DELETE")));

        AddIdentityDirectory(services, configuration);

        services.AddScoped<MemberService>();
        services.AddScoped<HouseholdService>();
        services.AddProblemDetails();

        return services;
    }

    private static void AddIdentityDirectory(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KeycloakAdminOptions>(configuration.GetSection(KeycloakAdminOptions.Section));

        var options = configuration.GetSection(KeycloakAdminOptions.Section).Get<KeycloakAdminOptions>()
            ?? new KeycloakAdminOptions();
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        services.AddHttpClient(ServiceTokenProvider.HttpClientName, http => http.Timeout = timeout);
        services.AddSingleton<ServiceTokenProvider>();
        services.AddTransient<ServiceTokenHandler>();

        services.AddHttpClient<IIdentityDirectory, KeycloakIdentityDirectory>(http =>
            {
                http.BaseAddress = options.BaseUrl
                    ?? throw new InvalidOperationException("« KeycloakAdmin:BaseUrl » est requis.");
                http.Timeout = timeout;
            })
            .AddHttpMessageHandler<ServiceTokenHandler>();

        // Même jeton de service que pour Keycloak : c'est lui qui porte le
        // rôle de provisionnement et l'audience catalogue-api.
        services.Configure<CatalogueOptions>(configuration.GetSection(CatalogueOptions.Section));
        var catalogue = configuration.GetSection(CatalogueOptions.Section).Get<CatalogueOptions>() ?? new CatalogueOptions();

        services.AddHttpClient<ICatalogueProvisioning, CatalogueProvisioningClient>(http =>
            {
                http.BaseAddress = catalogue.BaseUrl
                    ?? throw new InvalidOperationException("« Catalogue:BaseUrl » est requis.");
                // Jouer les migrations d'un schéma neuf prend plus qu'un appel ordinaire.
                http.Timeout = TimeSpan.FromSeconds(catalogue.TimeoutSeconds);
            })
            .AddHttpMessageHandler<ServiceTokenHandler>();
    }

    public static WebApplication UseTenantsApi(this WebApplication app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            // Un corps illisible (rôle inconnu, JSON mal formé) est une
            // erreur du client, y compris en développement où l'hôte la
            // lève au lieu d'y répondre.
            StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
                ? badRequest.StatusCode
                : StatusCodes.Status500InternalServerError
        });
        app.UseStatusCodePages();
        app.UseCors();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();

        return app;
    }
}
