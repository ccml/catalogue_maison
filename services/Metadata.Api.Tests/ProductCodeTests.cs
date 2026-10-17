using Metadata.Api.Domain;
using Xunit;

namespace Metadata.Api.Tests;

public sealed class ProductCodeTests
{
    [Theory]
    [InlineData("978-2-205-05925-0")]
    [InlineData("9782205059250")]
    [InlineData("2-205-05925-4")]
    [InlineData("2205059254")]
    public void Les_formes_d_un_meme_ISBN_donnent_la_meme_cle(string raw)
    {
        // Sinon le cache se fragmente et la source est interrogée autant de
        // fois qu'il y a de façons d'écrire l'ISBN.
        Assert.True(ProductCode.TryParse(raw, out var code));
        Assert.Equal("9782205059250", code!.Value);
        Assert.Equal(ProductCodeKind.Isbn, code.Kind);
        Assert.Equal("2205059254", code.Isbn10);
    }

    [Fact]
    public void Un_ISBN_10_a_cle_X_est_converti()
    {
        Assert.True(ProductCode.TryParse("0-8044-2957-X", out var code));
        Assert.Equal("9780804429573", code!.Value);
        Assert.Equal("080442957X", code.Isbn10);
    }

    [Fact]
    public void Un_ISBN_979_n_a_pas_d_equivalent_10()
    {
        Assert.True(ProductCode.TryParse("979-10-90636-07-1", out var code));
        Assert.Equal(ProductCodeKind.Isbn, code!.Kind);
        Assert.Null(code.Isbn10);
    }

    [Theory]
    [InlineData("0724384260958")]
    [InlineData("724384260958")]
    [InlineData("96385074")]
    public void Un_code_barres_commercial_n_est_pas_un_ISBN(string raw)
    {
        Assert.True(ProductCode.TryParse(raw, out var code));
        Assert.Equal(ProductCodeKind.Ean, code!.Kind);
        Assert.Equal(raw, code.Value);
        Assert.Null(code.Isbn10);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("97822050592501")]
    [InlineData("97822050X9250")]
    [InlineData("9782205059250; drop table")]
    public void Ce_qui_n_est_pas_un_code_est_refuse(string? raw)
    {
        Assert.False(ProductCode.TryParse(raw, out var code));
        Assert.Null(code);
    }
}
