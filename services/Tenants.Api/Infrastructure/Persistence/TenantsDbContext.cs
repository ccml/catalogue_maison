using Microsoft.EntityFrameworkCore;

namespace Tenants.Api.Infrastructure.Persistence;

/// <summary>
/// Registre des ménages, dans la base <c>tenants</c> : une seule base, non
/// partitionnée. C'est lui qu'il faut lire <em>avant</em> de savoir quel
/// schéma adresser ; il ne peut donc pas être lui-même partitionné par
/// ménage.
/// <para>
/// Aucun identifiant de connexion ici, ni mot de passe, ni rôle : le compte
/// et ses rôles vivent dans Keycloak. Le registre dit seulement qui
/// appartient à quel ménage.
/// </para>
/// </summary>
public sealed class TenantsDbContext(DbContextOptions<TenantsDbContext> options) : DbContext(options)
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Household> Households => Set<Household>();

    public DbSet<Member> Members => Set<Member>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var household = modelBuilder.Entity<Household>();

        household.ToTable("households");
        household.HasKey(h => h.Id);
        household.Property(h => h.Id).HasMaxLength(48);
        household.Property(h => h.DisplayName).HasMaxLength(120).IsRequired();
        household.Property(h => h.CreatedAt).IsRequired();

        var member = modelBuilder.Entity<Member>();

        member.ToTable("members");
        // Le sub Keycloak est la clé : c'est lui qui rend l'inscription
        // idempotente, une opération rejouée retombe sur la même ligne.
        member.HasKey(m => m.Subject);
        member.Property(m => m.Subject).ValueGeneratedNever();
        member.Property(m => m.HouseholdId).HasMaxLength(48).IsRequired();
        member.Property(m => m.Username).HasMaxLength(255).IsRequired();
        member.Property(m => m.RegisteredAt).IsRequired();
        member.HasIndex(m => m.HouseholdId);
        member.HasOne<Household>()
              .WithMany()
              .HasForeignKey(m => m.HouseholdId)
              .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Un ménage inscrit au registre. Son identifiant est celui du claim <c>tenant</c>.</summary>
public sealed class Household
{
    public required string Id { get; init; }

    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Rattachement d'un compte Keycloak à un ménage.</summary>
public sealed class Member
{
    /// <summary>Le <c>sub</c> du compte Keycloak.</summary>
    public Guid Subject { get; init; }

    public required string HouseholdId { get; init; }

    /// <summary>
    /// Copie de l'identifiant de connexion au moment de l'inscription. Sert
    /// à reconnaître une entrée dont le compte aurait disparu ; Keycloak
    /// reste la référence.
    /// </summary>
    public required string Username { get; init; }

    public DateTimeOffset RegisteredAt { get; init; }

    /// <summary>Renseigné tant que le compte est désactivé.</summary>
    public DateTimeOffset? DeactivatedAt { get; set; }
}
