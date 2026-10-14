using Catalogue.Api.Domain;
using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Catalogue.Api.Infrastructure.Persistence;

/// <summary>
/// Contexte du catalogue d'<strong>un</strong> ménage. Il ne peut pas être
/// construit sans ménage résolu : c'est la garantie que rien ne s'exécute
/// hors d'un schéma désigné.
/// </summary>
public sealed class CatalogueDbContext : DbContext
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public CatalogueDbContext(DbContextOptions<CatalogueDbContext> options, TenantId tenant)
        : base(options)
    {
        Tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
    }

    public TenantId Tenant { get; }

    public DbSet<Media> Medias => Set<Media>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Le schéma EST la frontière entre ménages. Il est porté par le
        // modèle, donc inscrit dans chaque requête SQL émise — et non par
        // l'état de la connexion, qui se partage dans le pool et se
        // réinitialise sans prévenir.
        modelBuilder.HasDefaultSchema(Tenant.Value);

        var media = modelBuilder.Entity<Media>();

        media.ToTable("medias");
        media.HasKey(m => m.Id);

        media.Property(m => m.Type)
             .HasConversion<string>()
             .HasMaxLength(32)
             .IsRequired();

        media.Property(m => m.Title).HasMaxLength(512).IsRequired();
        media.Property(m => m.Creator).HasMaxLength(512);
        media.Property(m => m.Location).HasMaxLength(256);
        media.Property(m => m.AddedAt).IsRequired();
        media.Property(m => m.IsLent).IsRequired();

        media.Property(m => m.Specifics)
             .HasColumnName("specifics")
             .HasColumnType("jsonb")
             .HasConversion(
                 specifics => MediaSpecificsSerializer.Serialize(specifics),
                 json => MediaSpecificsSerializer.Deserialize(json));

        // Promotion de l'ISBN en colonne générée indexée : c'est le critère
        // de recherche du chemin « scan ». Calculée par PostgreSQL, jamais
        // écrite depuis le C#.
        media.Property(m => m.Isbn)
             .HasColumnName("isbn")
             .HasMaxLength(20)
             .HasComputedColumnSql("specifics ->> 'isbn'", stored: true);

        media.HasIndex(m => m.Isbn);
        media.HasIndex(m => m.Type);
        media.HasIndex(m => m.AddedAt);
    }
}
