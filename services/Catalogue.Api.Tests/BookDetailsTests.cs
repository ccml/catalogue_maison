using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Infrastructure;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>Série, collection et genre d'un livre.</summary>
public sealed class BookDetailsTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    [Fact]
    public async Task Un_roman_est_trouve_par_sa_serie_et_ses_details_survivent_a_un_aller_retour()
    {
        // Même clé JSON que la série d'une BD : la colonne générée la lit,
        // la recherche par série vaut donc pour les livres sans migration.
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Book, "Le Prisonnier d'Azkaban", "J. K. Rowling", 1999, "Chambre",
            new BookSpecifics
            {
                Series = "Harry Potter",
                Volume = 3,
                Collection = "Folio junior",
                CollectionNumber = "1006",
                Genre = "Roman"
            }), CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        var recherche = await client.GetFromJsonAsync<PagedResponse<MediaSummaryResponse>>(
            "/medias?q=Harry%20Potter", CatalogueApiFixture.Json);
        var trouve = Assert.Single(recherche!.Items);

        var relu = await client.GetFromJsonAsync<MediaDetailResponse>($"/medias/{trouve.Id}", CatalogueApiFixture.Json);
        var book = Assert.IsType<BookSpecifics>(relu!.Specifics);
        Assert.Equal("Harry Potter", book.Series);
        Assert.Equal(3, book.Volume);
        Assert.Equal("Folio junior", book.Collection);
        Assert.Equal("1006", book.CollectionNumber);
        Assert.Equal("Roman", book.Genre);
    }

    [Fact]
    public void Un_tome_nul_ou_une_serie_trop_longue_sont_refuses()
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

        var tome = Assert.Throws<MediaValidationException>(() => Media.Create(
            MediaType.Book, "Titre", null, null, null, new BookSpecifics { Volume = 0 }, now));
        Assert.Contains("Le numéro de tome doit être strictement positif.", tome.Errors);

        // Plus longue que la colonne générée series : refusée à l'entrée,
        // plutôt qu'en échec d'écriture en base.
        Assert.Throws<MediaValidationException>(() => Media.Create(
            MediaType.Book, "Titre", null, null, null, new BookSpecifics { Series = new string('S', 300) }, now));
        Assert.Throws<MediaValidationException>(() => Media.Create(
            MediaType.ComicBook, "Titre", null, null, null, new ComicBookSpecifics { Series = new string('S', 300) }, now));
    }

    [Fact]
    public async Task La_collection_et_l_edition_d_une_BD_survivent_a_un_aller_retour()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdB, Roles.Adult);
        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.ComicBook, "Astérix chez les Belges", "René Goscinny", 2017, "Salon",
            new ComicBookSpecifics
            {
                Series = "Astérix",
                Collection = "Albums Astérix",
                CollectionNumber = "24",
                Edition = "Éd. de luxe"
            }), CatalogueApiFixture.Json);
        var cree = await creation.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);

        var relu = await client.GetFromJsonAsync<MediaDetailResponse>($"/medias/{cree!.Id}", CatalogueApiFixture.Json);

        var comic = Assert.IsType<ComicBookSpecifics>(relu!.Specifics);
        Assert.Equal("Albums Astérix", comic.Collection);
        Assert.Equal("24", comic.CollectionNumber);
        Assert.Equal("Éd. de luxe", comic.Edition);
    }
}
