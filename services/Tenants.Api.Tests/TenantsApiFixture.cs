using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tenants.Api.Domain;
using Tenants.Api.Infrastructure.Keycloak;
using Tenants.Api.Infrastructure.Persistence;
using Tenants.Api.Infrastructure.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace Tenants.Api.Tests;

/// <summary>
/// Monte Tenants.Api contre un PostgreSQL réel, migrations appliquées, et
/// remplace Keycloak par un annuaire en mémoire. Les validations de jeton
/// restent toutes actives : seule la provenance de la clé de signature
/// change.
/// </summary>
public sealed class TenantsApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "https://tests.cataloguemaison.local/realms/maison";
    public const string Audience = "tenants-api";

    public const string HouseholdA = "martin";
    public const string HouseholdB = "durand";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "tests" };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("tenants")
        .WithUsername("maison")
        .WithPassword("dev")
        .Build();

    public FakeIdentityDirectory Directory { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public TenantsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<TenantsDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql
            .MigrationsHistoryTable(TenantsDbContext.MigrationsHistoryTable))
        .UseSnakeCaseNamingConvention()
        .Options);

    /// <summary>
    /// Repart d'un état connu : deux ménages inscrits, un administrateur
    /// inscrit dans chacun.
    /// </summary>
    public async Task<(Guid AdminA, Guid AdminB)> ResetAsync()
    {
        Directory.Reset();

        await using var db = CreateDbContext();
        await db.Members.ExecuteDeleteAsync();
        await db.Households.ExecuteDeleteAsync();

        db.Households.AddRange(
            new Household { Id = HouseholdA, DisplayName = "Martin", CreatedAt = DateTimeOffset.UtcNow },
            new Household { Id = HouseholdB, DisplayName = "Durand", CreatedAt = DateTimeOffset.UtcNow });

        var adminA = Directory.Seed("alice", HouseholdA, Roles.Adult, Roles.HouseholdAdmin);
        var adminB = Directory.Seed("bruno", HouseholdB, Roles.Adult, Roles.HouseholdAdmin);

        db.Members.AddRange(
            new Member { Subject = adminA, HouseholdId = HouseholdA, Username = "alice", RegisteredAt = DateTimeOffset.UtcNow },
            new Member { Subject = adminB, HouseholdId = HouseholdB, Username = "bruno", RegisteredAt = DateTimeOffset.UtcNow });

        await db.SaveChangesAsync();
        return (adminA, adminB);
    }

    /// <summary>Client porteur d'un jeton d'utilisateur du ménage donné.</summary>
    public HttpClient ClientFor(Guid subject, string? tenant, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(subject, tenant, roles));
        return client;
    }

    public static string Token(Guid subject, string? tenant, string[] roles, string audience = Audience)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject.ToString(),
            ["preferred_username"] = $"test-{subject:N}",
            ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles }
        };

        if (tenant is not null)
        {
            claims["tenant"] = tenant;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Tenants", _postgres.GetConnectionString());
        builder.UseSetting("Keycloak:Authority", Issuer);
        builder.UseSetting("Keycloak:Audience", Audience);
        builder.UseSetting("Keycloak:RequireHttpsMetadata", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIdentityDirectory>();
            services.AddSingleton<IIdentityDirectory>(Directory);

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

/// <summary>
/// Annuaire en mémoire, au comportement calqué sur l'Admin REST API telle
/// qu'observée : identifiants uniques dans le realm, rôles par nom.
/// </summary>
public sealed class FakeIdentityDirectory : IIdentityDirectory
{
    private readonly ConcurrentDictionary<Guid, Account> _accounts = new();

    public sealed class Account
    {
        public required Guid Id { get; init; }
        public required string Username { get; init; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Tenant { get; set; }
        public HashSet<string> Roles { get; } = new(StringComparer.Ordinal);
        public string? Password { get; set; }
        public bool PasswordTemporary { get; set; }
        public int SignOuts { get; set; }
    }

    /// <summary>Faire échouer la prochaine attribution de rôles, pour simuler une panne en cours d'opération.</summary>
    public bool FailNextRoleUpdate { get; set; }

    public IReadOnlyCollection<Account> Accounts => _accounts.Values.ToList();

    public Account this[Guid id] => _accounts[id];

    public Account? ByUsername(string username) =>
        _accounts.Values.SingleOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

    public Guid Seed(string username, string? tenant, params string[] roles)
    {
        var account = new Account { Id = Guid.NewGuid(), Username = username, FirstName = username, Tenant = tenant };
        account.Roles.UnionWith(roles);
        _accounts[account.Id] = account;
        return account.Id;
    }

    public void Reset()
    {
        _accounts.Clear();
        FailNextRoleUpdate = false;
    }

    public Task<IReadOnlyList<DirectoryAccount>> ListByTenantAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DirectoryAccount>>(_accounts.Values
            .Where(a => a.Tenant == tenant.Value)
            .Select(ToDirectory)
            .ToList());

    public Task<DirectoryAccount?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(id, out var account) ? ToDirectory(account) : null);

    public Task<DirectoryAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
        Task.FromResult(ByUsername(username) is { } account ? ToDirectory(account) : null);

    public Task<Guid> CreateAsync(NewAccount account, CancellationToken cancellationToken)
    {
        if (ByUsername(account.Username) is not null)
        {
            throw new UsernameTakenException(account.Username);
        }

        var created = new Account
        {
            Id = Guid.NewGuid(),
            Username = account.Username,
            FirstName = account.FirstName,
            LastName = account.LastName,
            Tenant = account.Tenant.Value,
            Password = account.TemporaryPassword,
            PasswordTemporary = true
        };

        _accounts[created.Id] = created;
        return Task.FromResult(created.Id);
    }

    public Task<IReadOnlySet<string>> GetRealmRolesAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(_accounts[id].Roles.ToHashSet(StringComparer.Ordinal));

    public Task SetManagedRolesAsync(Guid id, IReadOnlySet<string> wanted, CancellationToken cancellationToken)
    {
        if (FailNextRoleUpdate)
        {
            FailNextRoleUpdate = false;
            throw new IdentityDirectoryException("panne simulée.");
        }

        if (wanted.Except(Roles.Managed).Any())
        {
            throw new ArgumentException("Rôle hors périmètre.", nameof(wanted));
        }

        var roles = _accounts[id].Roles;
        roles.RemoveWhere(r => Roles.Managed.Contains(r) && !wanted.Contains(r));
        roles.UnionWith(wanted);
        return Task.CompletedTask;
    }

    public Task SetTemporaryPasswordAsync(Guid id, string password, CancellationToken cancellationToken)
    {
        _accounts[id].Password = password;
        _accounts[id].PasswordTemporary = true;
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        _accounts[id].Enabled = enabled;
        return Task.CompletedTask;
    }

    public Task SignOutEverywhereAsync(Guid id, CancellationToken cancellationToken)
    {
        _accounts[id].SignOuts++;
        return Task.CompletedTask;
    }

    private static DirectoryAccount ToDirectory(Account a) =>
        new(a.Id, a.Username, a.FirstName, a.LastName, a.Enabled, a.Tenant);
}
