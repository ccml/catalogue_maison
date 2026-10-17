using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Metadata.Api.Infrastructure.Persistence;

/// <summary>
/// Utilisé par <c>dotnet ef</c>. La base est désignée par
/// <c>METADATA_CONNECTION</c>, ou par la base locale de développement.
/// </summary>
public sealed class MetadataDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MetadataDbContext>
{
    public MetadataDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("METADATA_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=metadata;Username=maison;Password=dev";

        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(MetadataDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new MetadataDbContext(options);
    }
}
