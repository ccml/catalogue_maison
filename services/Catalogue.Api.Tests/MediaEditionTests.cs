using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Infrastructure;
using Xunit;

namespace Catalogue.Api.Tests;

public sealed class MediaEditionTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private async Task<MediaDetailResponse> Enregistre(HttpClient client, CreateMediaRequest requete)
    {
        var reponse = await client.PostAsJsonAsync("/medias", requete, CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        var cree = await reponse.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);
        Assert.NotNull(cree);
        return cree!;
    }

    [Fact]
    public async Task Une_fiche_corrigee_garde_sa_date_d_ajout_et_son_etat_de_pret()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var cree = await Enregistre(client, new CreateMediaRequest(
            MediaType.Film, "Stalker", "Andrei Tarkovski", 1979, "Salon"));

        var pret = await client.PutAsJsonAsync($"/medias/{cree.Id}/lent", new SetLentRequest(true),
            CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.NoContent, pret.StatusCode);

        var corrige = await client.PutAsJsonAsync($"/medias/{cree.Id}", new UpdateMediaRequest(
            MediaType.Film, "Stalker", "Andreï Tarkovski", 1979, "Bureau",
            new FilmSpecifics { Studio = "Mosfilm", DurationMinutes = 161 }),
            CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.OK, corrige.StatusCode);

        var relu = await corrige.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);
        Assert.NotNull(relu);

        Assert.Equal("Andreï Tarkovski", relu!.Creator);
        Assert.Equal("Bureau", relu.Location);

        // La date d'ajout date l'entrée au catalogue, pas la dernière
        // correction ; et corriger une fiche ne fait pas revenir un prêt.
        Assert.Equal(cree.AddedAt, relu.AddedAt);
        Assert.True(relu.IsLent);
    }

    [Fact]
    public async Task Un_livre_pris_pour_une_bd_se_corrige_en_changeant_son_type()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var cree = await Enregistre(client, new CreateMediaRequest(
            MediaType.Book, "Astérix le Gaulois", "René Goscinny", 1961, "Salon",
            new BookSpecifics { Isbn = "9782012101333", Publisher = "Hachette" }));

        var corrige = await client.PutAsJsonAsync($"/medias/{cree.Id}", new UpdateMediaRequest(
            MediaType.ComicBook, "Astérix le Gaulois", "René Goscinny", 1961, "Salon",
            new ComicBookSpecifics
            {
                Series = "Astérix",
                Volume = 1,
                Illustrator = "Albert Uderzo",
                Publisher = "Hachette",
                Isbn = "9782012101333",
            }),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.OK, corrige.StatusCode);
        var relu = await corrige.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);

        Assert.Equal(MediaType.ComicBook, relu!.Type);
        var specificites = Assert.IsType<ComicBookSpecifics>(relu.Specifics);
        Assert.Equal("Astérix", specificites.Series);

        // La série est désormais indexée : la recherche doit la voir.
        var parSerie = await client.GetFromJsonAsync<PagedResponse<MediaSummaryResponse>>(
            "/medias?q=Astérix", CatalogueApiFixture.Json);
        Assert.Contains(parSerie!.Items, media => media.Id == cree.Id);
    }

    [Fact]
    public async Task Changer_le_type_sans_adapter_les_specificites_est_refuse()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var cree = await Enregistre(client, new CreateMediaRequest(
            MediaType.Book, "Tintin au Tibet", "Hergé", 1960, null,
            new BookSpecifics { Publisher = "Casterman" }));

        var refuse = await client.PutAsJsonAsync($"/medias/{cree.Id}", new UpdateMediaRequest(
            MediaType.ComicBook, "Tintin au Tibet", "Hergé", 1960, null,
            new BookSpecifics { Publisher = "Casterman" }),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadRequest, refuse.StatusCode);
    }

    [Fact]
    public async Task Un_media_supprime_n_est_plus_trouvable()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var cree = await Enregistre(client, new CreateMediaRequest(
            MediaType.Game, "Les Aventuriers du rail", null, null, "Placard du salon"));

        var suppression = await client.DeleteAsync($"/medias/{cree.Id}");
        Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);

        var relecture = await client.GetAsync($"/medias/{cree.Id}");
        Assert.Equal(HttpStatusCode.NotFound, relecture.StatusCode);

        // La suppression est définitive : la rejouer ne trouve plus rien.
        var seconde = await client.DeleteAsync($"/medias/{cree.Id}");
        Assert.Equal(HttpStatusCode.NotFound, seconde.StatusCode);
    }

    [Fact]
    public async Task Un_enfant_ne_peut_ni_corriger_ni_supprimer()
    {
        var adulte = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var enfant = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Child);

        var cree = await Enregistre(adulte, new CreateMediaRequest(
            MediaType.Music, "Kind of Blue", "Miles Davis", 1959, "Salon"));

        // L'interface masque déjà ces gestes, mais c'est l'API qui tranche.
        var correction = await enfant.PutAsJsonAsync($"/medias/{cree.Id}", new UpdateMediaRequest(
            MediaType.Music, "Autre titre"), CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Forbidden, correction.StatusCode);

        var suppression = await enfant.DeleteAsync($"/medias/{cree.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, suppression.StatusCode);

        // Et la fiche est intacte.
        var relu = await adulte.GetFromJsonAsync<MediaDetailResponse>(
            $"/medias/{cree.Id}", CatalogueApiFixture.Json);
        Assert.Equal("Kind of Blue", relu!.Title);
    }
}
