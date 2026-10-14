using Catalogue.Api.Infrastructure.Tenancy;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// La valeur d'un <see cref="TenantId"/> finit dans un identifiant SQL : sa
/// validation est une frontière de sécurité, et se teste comme telle.
/// </summary>
public sealed class TenantIdTests
{
    [Theory]
    [InlineData("martin")]
    [InlineData("durand")]
    [InlineData("menage_2")]
    public void Une_forme_valide_est_acceptee(string valeur)
    {
        Assert.True(TenantId.TryCreate(valeur, out var tenant));
        Assert.Equal(valeur, tenant!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MARTIN")]
    [InlineData("2menages")]
    [InlineData("menage-2")]
    [InlineData("menage 2")]
    [InlineData("martin\"")]
    [InlineData("martin; drop schema durand cascade")]
    [InlineData("public")]
    [InlineData("information_schema")]
    [InlineData("pg_catalog")]
    public void Une_forme_invalide_est_refusee(string? valeur)
    {
        Assert.False(TenantId.TryCreate(valeur, out var tenant));
        Assert.Null(tenant);
    }

    [Fact]
    public void Un_identifiant_trop_long_est_refuse()
    {
        var trop_long = new string('a', TenantId.MaxLength + 1);

        Assert.False(TenantId.TryCreate(trop_long, out _));
    }
}
