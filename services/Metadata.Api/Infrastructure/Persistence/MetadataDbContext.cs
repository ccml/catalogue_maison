using System.Text.Json;
using System.Text.Json.Serialization;
using Metadata.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Metadata.Api.Infrastructure.Persistence;

/// <summary>
/// Cache d'enrichissement, dans la base <c>metadata</c>. Une seule base,
/// sans schéma par ménage : ce service est tenant-agnostique par
/// construction.
/// </summary>
public sealed class MetadataDbContext(DbContextOptions<MetadataDbContext> options) : DbContext(options)
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Forme de la notice stockée. Le camelCase fait partie du contrat de
    /// stockage : le changer rendrait illisibles les notices déjà en cache.
    /// </summary>
    internal static readonly JsonSerializerOptions RecordJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public DbSet<CachedLookup> Lookups => Set<CachedLookup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var lookup = modelBuilder.Entity<CachedLookup>();

        lookup.ToTable("lookups");
        lookup.HasKey(l => l.Code);
        lookup.Property(l => l.Code).HasMaxLength(13);
        lookup.Property(l => l.FetchedAt).IsRequired();

        // Une seule forme de notice : un ValueConverter suffit, sans la
        // polymorphie qu'exigent les spécificités du catalogue.
        lookup.Property(l => l.Record)
              .HasColumnType("jsonb")
              .HasConversion(
                  record => record == null ? null : JsonSerializer.Serialize(record, RecordJson),
                  json => json == null ? null : JsonSerializer.Deserialize<MetadataRecord>(json, RecordJson));
    }
}
