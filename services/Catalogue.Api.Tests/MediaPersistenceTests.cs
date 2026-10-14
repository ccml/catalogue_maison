using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Infrastructure;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Aller-retour des spécificités à travers une colonne <c>jsonb</c>.
/// <para>
/// Ces tests existent pour un piège précis : <c>jsonb</c> est stocké
/// décomposé et restitue ses clés triées, pas dans l'ordre d'écriture. Toute
/// sérialisation qui dépendrait de la position d'une clé — le discriminant en
/// premier, typiquement — passe en écriture et casse en relecture. Un test
/// qui ne ferait qu'un aller-retour en mémoire ne verrait rien.
/// </para>
/// </summary>
public sealed class MediaPersistenceTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    [Fact]
    public async Task Les_specificites_d_un_livre_survivent_a_un_aller_retour_en_base()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Book,
            "L'Œuvre au noir",
            "Marguerite Yourcenar",
            1968,
            "Bureau",
            new BookSpecifics { Isbn = "978-2-07-036798-8", Publisher = "Gallimard", Pages = 347 }),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var cree = await creation.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);
        Assert.NotNull(cree);

        // La relecture passe par la base, donc par jsonb.
        var relu = await client.GetFromJsonAsync<MediaDetailResponse>(
            $"/medias/{cree!.Id}", CatalogueApiFixture.Json);

        Assert.NotNull(relu);
        var specificites = Assert.IsType<BookSpecifics>(relu!.Specifics);
        Assert.Equal("9782070367988", specificites.Isbn);
        Assert.Equal("Gallimard", specificites.Publisher);
        Assert.Equal(347, specificites.Pages);
    }

    [Fact]
    public async Task La_colonne_generee_rend_le_livre_cherchable_par_son_isbn()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Book,
            "Les Fourmis",
            "Bernard Werber",
            1991,
            null,
            new BookSpecifics { Isbn = "9782226051570" }),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        // L'ISBN est fourni ici sous une forme séparée : la normalisation à
        // l'écriture et la colonne générée doivent se rejoindre.
        var trouve = await client.GetFromJsonAsync<MediaDetailResponse>(
            "/medias/by-isbn/978-2-226-05157-0", CatalogueApiFixture.Json);

        Assert.NotNull(trouve);
        Assert.Equal("Les Fourmis", trouve!.Title);
    }

    [Fact]
    public async Task Une_liste_dans_les_specificites_survit_a_un_aller_retour()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var pistes = new[] { "So What", "Freddie Freeloader", "Blue in Green" };

        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Music,
            "Kind of Blue",
            "Miles Davis",
            1959,
            "Salon",
            new MusicSpecifics { Label = "Columbia", Format = "Vinyle", Tracks = pistes }),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var cree = await creation.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);

        var relu = await client.GetFromJsonAsync<MediaDetailResponse>(
            $"/medias/{cree!.Id}", CatalogueApiFixture.Json);

        var specificites = Assert.IsType<MusicSpecifics>(relu!.Specifics);
        Assert.Equal(pistes, specificites.Tracks);
    }

    [Fact]
    public async Task Une_bd_est_trouvee_par_sa_serie_meme_si_le_titre_ne_la_porte_pas()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        // Le cas réel : on cherche « Blacksad », pas « Quelque part entre les
        // ombres ». Sans la colonne générée, cette recherche ne trouve rien.
        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.ComicBook,
            "Quelque part entre les ombres",
            "Juan Díaz Canales",
            2000,
            "Salon",
            new ComicBookSpecifics
            {
                Series = "Blacksad",
                Volume = 1,
                Illustrator = "Juanjo Guarnido",
                Publisher = "Dargaud",
                Isbn = "978-2-205-04966-6",
            }),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        var parSerie = await client.GetFromJsonAsync<PagedResponse<MediaSummaryResponse>>(
            "/medias?q=Blacksad", CatalogueApiFixture.Json);

        Assert.NotNull(parSerie);
        Assert.Single(parSerie.Items);
        Assert.Equal("Quelque part entre les ombres", parSerie.Items[0].Title);
    }

    [Fact]
    public async Task Les_specificites_d_une_bd_survivent_a_un_aller_retour_en_base()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.ComicBook,
            "Le Secret de la Licorne",
            "Hergé",
            1943,
            null,
            new ComicBookSpecifics
            {
                Series = "Les Aventures de Tintin",
                Volume = 11,
                Illustrator = "Hergé",
                Publisher = "Casterman",
                Isbn = "2 203 00110 6",
            }),
            CatalogueApiFixture.Json);

        var cree = await creation.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);
        Assert.NotNull(cree);

        var relu = await client.GetFromJsonAsync<MediaDetailResponse>(
            $"/medias/{cree!.Id}", CatalogueApiFixture.Json);

        var specificites = Assert.IsType<ComicBookSpecifics>(relu!.Specifics);
        Assert.Equal("Les Aventures de Tintin", specificites.Series);
        Assert.Equal(11, specificites.Volume);
        Assert.Equal("Hergé", specificites.Illustrator);

        // L'ISBN d'une BD est normalisé comme celui d'un livre, sinon la
        // colonne générée et le chemin « scan » ne le retrouvent pas.
        Assert.Equal("2203001106", specificites.Isbn);

        var parScan = await client.GetAsync("/medias/by-isbn/2-203-00110-6");
        Assert.Equal(HttpStatusCode.OK, parScan.StatusCode);
    }

    [Fact]
    public async Task Un_media_sans_specificites_est_relu_sans_specificites()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Film, "Stalker"),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var cree = await creation.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);

        var relu = await client.GetFromJsonAsync<MediaDetailResponse>(
            $"/medias/{cree!.Id}", CatalogueApiFixture.Json);

        Assert.NotNull(relu);
        Assert.Null(relu!.Specifics);
    }
}
