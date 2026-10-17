using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalogue.Api.Infrastructure.Metadata;
using Catalogue.Api.Infrastructure.Persistence;
using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Monte l'API contre un PostgreSQL réel et provisionne deux ménages. Un
/// faux dépôt en mémoire ne prouverait rien de l'isolation : elle est portée
/// par le schéma, donc par la base.
/// <para>
/// Le provisionnement emprunte le même chemin qu'en production — créer le
/// schéma, puis y appliquer les migrations — et non un raccourci de création
/// de tables depuis le modèle. C'est ce qui permet aux tests de valider la
/// garde sur la version du schéma.
/// </para>
/// </summary>
public sealed class CatalogueApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Ménages provisionnés : schéma créé et migrations appliquées.</summary>
    public const string HouseholdA = "martin";

    public const string HouseholdB = "durand";

    /// <summary>
    /// Ménage dont le jeton est valide mais dont le schéma n'a jamais été
    /// provisionné. Sert à vérifier que le service refuse de le servir.
    /// </summary>
    public const string HouseholdWithoutSchema = "sans_schema";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("catalogue")
        .WithUsername("maison")
        .WithPassword("dev")
        .Build();

    /// <summary>
    /// Metadata.Api simulé : Catalogue.Api n'en voit que le contrat, et ces
    /// tests portent sur ce qu'il en fait — pas sur l'enrichissement.
    /// </summary>
    public FakeMetadataLookup Metadata { get; } = new();

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
            await ProvisionAsync(tenant);
        }
    }

    /// <summary>
    /// Crée le schéma d'un ménage et y applique les migrations. Idempotent.
    /// </summary>
    public async Task ProvisionAsync(string tenant)
    {
        var tenantId = TenantId.Create(tenant);

        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // Le nom a passé la validation de TenantId ; la citation protège
            // du repli en minuscules de PostgreSQL.
            command.CommandText = $"CREATE SCHEMA IF NOT EXISTS \"{tenantId.Value}\"";
            await command.ExecuteNonQueryAsync();
        }

        await using var db = BuildContext(tenantId);
        await db.Database.MigrateAsync();
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
        // La chaîne de base ne porte pas de search_path : c'est
        // TenantConnectionStringFactory qui l'ajoute, par ménage.
        builder.UseSetting("ConnectionStrings:Catalogue", _postgres.GetConnectionString());
        builder.UseSetting("Keycloak:Authority", TestTokens.Issuer);
        builder.UseSetting("Keycloak:Audience", TestTokens.Audience);
        builder.UseSetting("Keycloak:AdditionalIssuers:0", TestTokens.RelayedIssuer);
        builder.UseSetting("Keycloak:RequireHttpsMetadata", "false");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IMetadataLookup>(Metadata);

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

    private CatalogueDbContext BuildContext(TenantId tenant)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            SearchPath = tenant.Value
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<CatalogueDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(CatalogueDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CatalogueDbContext(options, tenant);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>Résolution de notices pilotée par le test, qui garde trace des codes demandés.</summary>
public sealed class FakeMetadataLookup : IMetadataLookup
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _requested = new();

    public Func<string, MetadataLookup> Answer { get; set; } = _ => MetadataLookup.NotFound;

    public IReadOnlyCollection<string> Requested => _requested;

    public Task<MetadataLookup> LookupAsync(string code, CancellationToken cancellationToken)
    {
        _requested.Enqueue(code);
        return Task.FromResult(Answer(code));
    }

    public bool FilmSearchEnabled { get; set; } = true;

    public Func<string, FilmSearch> FilmSearchAnswer { get; set; } = _ => new FilmSearch(MetadataLookupOutcome.Found, []);

    public Func<string, MetadataLookup> FilmAnswer { get; set; } = _ => MetadataLookup.NotFound;

    public Task<bool> FilmSearchEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(FilmSearchEnabled);

    public Task<FilmSearch> SearchFilmsAsync(string title, CancellationToken cancellationToken) =>
        Task.FromResult(FilmSearchAnswer(title));

    public Task<MetadataLookup> GetFilmAsync(string imdbId, CancellationToken cancellationToken) =>
        Task.FromResult(FilmAnswer(imdbId));
}
