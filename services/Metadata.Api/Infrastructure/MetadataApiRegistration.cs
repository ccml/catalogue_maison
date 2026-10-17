using System.Text.Json;
using System.Text.Json.Serialization;
using Metadata.Api.Features.Lookups;
using Metadata.Api.Infrastructure.Persistence;
using Metadata.Api.Infrastructure.Sources;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Metadata.Api.Infrastructure;

public static class MetadataApiRegistration
{
    public const string ConnectionName = "Metadata";

    public static IServiceCollection AddMetadataApi(this IServiceCollection services, IConfiguration configuration)
    {
        var keycloak = configuration.GetSection(KeycloakOptions.Section).Get<KeycloakOptions>()
            ?? throw new InvalidOperationException("Section de configuration « Keycloak » absente.");

        if (string.IsNullOrWhiteSpace(keycloak.Authority) || string.IsNullOrWhiteSpace(keycloak.Audience))
        {
            throw new InvalidOperationException("« Keycloak:Authority » et « Keycloak:Audience » sont requis.");
        }

        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.Section));
        services.Configure<EnrichmentOptions>(configuration.GetSection(EnrichmentOptions.Section));

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddSingleton(TimeProvider.System);
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
                    NameClaimType = "azp",
                    RoleClaimType = "roles"
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.MetadataRead, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Enrichment));

            options.FallbackPolicy = options.GetPolicy(Policies.MetadataRead);
        });

        services.AddDbContext<MetadataDbContext>(options => options
            .UseNpgsql(
                configuration.GetConnectionString(ConnectionName)
                    ?? throw new InvalidOperationException(
                        $"Chaîne de connexion « {ConnectionName} » absente de la configuration."),
                npgsql => npgsql.MigrationsHistoryTable(MetadataDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention());

        AddSources(services);
        services.AddScoped<LookupService>();
        services.AddProblemDetails();

        return services;
    }

    /// <summary>
    /// L'ordre d'enregistrement est l'ordre d'interrogation : la BnF avant
    /// Open Library pour les ISBN, MusicBrainz pour le reste.
    /// </summary>
    private static void AddSources(IServiceCollection services)
    {
        services.AddSingleton<MusicBrainzRateGate>();

        services.AddHttpClient<BnfSource>((serviceProvider, http) =>
            Configure(http, serviceProvider, o => o.BnfBaseUrl));

        services.AddHttpClient<OpenLibrarySource>((serviceProvider, http) =>
            Configure(http, serviceProvider, o => o.OpenLibraryBaseUrl));

        services.AddHttpClient<MusicBrainzSource>((serviceProvider, http) =>
        {
            Configure(http, serviceProvider, o => o.MusicBrainzBaseUrl);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                serviceProvider.GetRequiredService<IOptions<EnrichmentOptions>>().Value.MusicBrainzUserAgent);
        });

        services.AddTransient<IMetadataSource>(sp => sp.GetRequiredService<BnfSource>());
        services.AddTransient<IMetadataSource>(sp => sp.GetRequiredService<OpenLibrarySource>());
        services.AddTransient<IMetadataSource>(sp => sp.GetRequiredService<MusicBrainzSource>());
    }

    private static void Configure(HttpClient http, IServiceProvider serviceProvider, Func<EnrichmentOptions, Uri> baseUrl)
    {
        var options = serviceProvider.GetRequiredService<IOptions<EnrichmentOptions>>().Value;
        http.BaseAddress = baseUrl(options);
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }

    public static WebApplication UseMetadataApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
