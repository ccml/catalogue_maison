using Catalogue.Api.Domain;
using Xunit;

namespace Catalogue.Api.Tests;

public sealed class MediaTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Le_type_et_le_titre_suffisent_a_creer_un_media()
    {
        var media = Media.Create(MediaType.Film, "Alien", null, null, null, null, Maintenant);

        Assert.Equal(MediaType.Film, media.Type);
        Assert.Equal("Alien", media.Title);
        Assert.False(media.IsLent);
        Assert.Null(media.Specifics);
    }

    [Fact]
    public void Un_titre_vide_est_refuse()
    {
        var exception = Assert.Throws<MediaValidationException>(
            () => Media.Create(MediaType.Book, "   ", null, null, null, null, Maintenant));

        Assert.Contains("Le titre est requis.", exception.Errors);
    }

    [Fact]
    public void Des_specificites_d_un_autre_type_sont_refusees()
    {
        var exception = Assert.Throws<MediaValidationException>(
            () => Media.Create(MediaType.Book, "Dune", null, null, null,
                new FilmSpecifics { Studio = "Legendary" }, Maintenant));

        Assert.Single(exception.Errors);
    }

    [Theory]
    [InlineData("978-2-07-040827-8", "9782070408278")]
    [InlineData("2 07 040827 X", "207040827X")]
    [InlineData("  9782070408278  ", "9782070408278")]
    public void L_isbn_est_normalise_a_la_creation(string saisi, string attendu)
    {
        var media = Media.Create(MediaType.Book, "Le Horla", null, null, null,
            new BookSpecifics { Isbn = saisi }, Maintenant);

        var specificites = Assert.IsType<BookSpecifics>(media.Specifics);
        Assert.Equal(attendu, specificites.Isbn);
    }

    [Fact]
    public void Un_isbn_de_longueur_invalide_est_refuse()
    {
        var exception = Assert.Throws<MediaValidationException>(
            () => Media.Create(MediaType.Book, "Le Horla", null, null, null,
                new BookSpecifics { Isbn = "12345" }, Maintenant));

        Assert.Single(exception.Errors);
    }

    [Fact]
    public void Un_tome_negatif_est_refuse()
    {
        var exception = Assert.Throws<MediaValidationException>(
            () => Media.Create(MediaType.ComicBook, "Astérix le Gaulois", "René Goscinny", 1961, null,
                new ComicBookSpecifics { Series = "Astérix", Volume = 0 }, Maintenant));

        Assert.Single(exception.Errors);
    }

    [Fact]
    public void Des_specificites_de_livre_sont_refusees_pour_une_bd()
    {
        // Les deux types se ressemblent — raison de plus pour que le
        // discriminant soit vérifié.
        var exception = Assert.Throws<MediaValidationException>(
            () => Media.Create(MediaType.ComicBook, "Astérix le Gaulois", null, null, null,
                new BookSpecifics { Isbn = "9782012101333" }, Maintenant));

        Assert.Single(exception.Errors);
    }

    [Fact]
    public void Une_annee_hors_bornes_est_refusee()
    {
        var exception = Assert.Throws<MediaValidationException>(
            () => Media.Create(MediaType.Music, "Kind of Blue", "Miles Davis", 1200, null, null, Maintenant));

        Assert.Single(exception.Errors);
    }
}
