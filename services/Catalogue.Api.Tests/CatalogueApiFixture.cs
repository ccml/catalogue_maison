using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalogue.Api.Infrastructure.Persistence;
using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Monte l'API contre un PostgreSQL réel et provisionne deux ménages. Un
/// faux dépôt en mémoire ne prouverait rien de l'isolation : elle est portée
/// par le schéma, donc par la base.
/// </summary>
public sealed class CatalogueApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Ménages provisionnés pour les tests.</summary>
    public const string HouseholdA = "martin";

    public const string HouseholdB = "durand";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("catalogue")
        .WithUsername("maison")
        .WithPassword("dev")
        .Build();

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(), new MediaSpecificsJsonConverter() }
    };

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        foreach (var tenant in new[] { HouseholdA, HouseholdB })
        {
            await using var db = BuildContext(tenant);

            // CreateTables, et non EnsureCreated : plusieurs schémas vivent
            // dans la même base, et EnsureCreated ne crée rien dès lors que
            // la base existe déjà.
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }
    }

    /// <summary>Client HTTP porteur d'un jeton valide pour le ménage demandé.</summary>
    public HttpClient ClientFor(string tenant, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.ForHousehold(tenant, roles));
        return client;
    }

    /// <summary>Client HTTP porteur d'un jeton fourni tel quel.</summary>
    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Catalogue", _postgres.GetConnectionString());
        builder.UseSetting("Keycloak:Authority", TestTokens.Issuer);
        builder.UseSetting("Keycloak:Audience", TestTokens.Audience);
        builder.UseSetting("Keycloak:RequireHttpsMetadata", "false");

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // On remplace la découverte du JWKS par la clé de test, sans
                // toucher aux validations elles-mêmes.
                options.Authority = null;
                options.MetadataAddress = null!;
                options.ConfigurationManager = null;
                options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
                options.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                options.TokenValidationParameters.ValidAudience = TestTokens.Audience;
            });
        });
    }

    private CatalogueDbContext BuildContext(string tenant)
    {
        var options = new DbContextOptionsBuilder<CatalogueDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql
                .MigrationsHistoryTable(CatalogueDbContext.MigrationsHistoryTable, tenant))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>()
            .Options;

        return new CatalogueDbContext(options, TenantId.Create(tenant));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
