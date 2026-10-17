using System.Net.Http.Headers;
using System.Security.Cryptography;
using Metadata.Api.Domain;
using Metadata.Api.Infrastructure.Persistence;
using Metadata.Api.Infrastructure.Sources;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;

namespace Metadata.Api.Tests;

/// <summary>
/// Monte Metadata.Api contre un PostgreSQL réel, migrations appliquées, et
/// remplace les sources externes par des sources pilotées par le test.
/// Les validations de jeton restent toutes actives : seule la provenance de
/// la clé de signature change.
/// </summary>
public sealed class MetadataApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "https://tests.cataloguemaison.local/realms/maison";
    public const string Audience = "metadata-api";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "tests" };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("metadata")
        .WithUsername("maison")
        .WithPassword("dev")
        .Build();

    /// <summary>Source des ISBN, interrogée en premier.</summary>
    public FakeSource Books { get; } = new("livres", ProductCodeKind.Isbn);

    /// <summary>Seconde source des ISBN, en repli.</summary>
    public FakeSource BooksFallback { get; } = new("livres-repli", ProductCodeKind.Isbn);

    public FakeSource Music { get; } = new("disques", ProductCodeKind.Ean);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql
                .MigrationsHistoryTable(MetadataDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options);

        await db.Database.MigrateAsync();
    }

    /// <summary>Client porteur du jeton de service de <c>catalogue-api</c>.</summary>
    public HttpClient ServiceClient() => ClientWithToken(Token(Audience, ["enrichissement"]));

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static string Token(string audience, string[] roles, Dictionary<string, object>? extra = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["azp"] = "catalogue-api",
            ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles }
        };

        foreach (var (name, value) in extra ?? [])
        {
            claims[name] = value;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
            Claims = claims
        });
    }

    /// <summary>Vide le cache et remet les sources à zéro entre deux tests.</summary>
    public async Task ResetAsync()
    {
        Books.Reset();
        BooksFallback.Reset();
        Music.Reset();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        await db.Lookups.ExecuteDeleteAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Metadata", _postgres.GetConnectionString());
        builder.UseSetting("Keycloak:Authority", Issuer);
        builder.UseSetting("Keycloak:Audience", Audience);
        builder.UseSetting("Keycloak:RequireHttpsMetadata", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMetadataSource>();
            services.AddSingleton<IMetadataSource>(Books);
            services.AddSingleton<IMetadataSource>(BooksFallback);
            services.AddSingleton<IMetadataSource>(Music);

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.MetadataAddress = null!;
                options.ConfigurationManager = null;
                options.TokenValidationParameters.IssuerSigningKey = Key;
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = Audience;
            });
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>Source pilotée par le test : elle répond ce qu'on lui dit, et compte ses appels.</summary>
public sealed class FakeSource(string name, ProductCodeKind kind) : IMetadataSource
{
    private int _calls;

    public string Name => name;

    public int Calls => _calls;

    /// <summary>Notice renvoyée ; <c>null</c> pour « code inconnu ».</summary>
    public Func<ProductCode, MetadataRecord?> Answer { get; set; } = _ => null;

    /// <summary>Vrai pour simuler une source en panne.</summary>
    public bool Failing { get; set; }

    public bool Supports(ProductCode code) => code.Kind == kind;

    public Task<MetadataRecord?> LookupAsync(ProductCode code, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        if (Failing)
        {
            throw new MetadataSourceException(name, "panne simulée.");
        }

        return Task.FromResult(Answer(code));
    }

    public void Reset()
    {
        _calls = 0;
        Failing = false;
        Answer = _ => null;
    }
}
