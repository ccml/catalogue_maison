using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Films;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Metadata;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>Recherche d'un film par son titre, et fiche pré-remplie.</summary>
public sealed class FilmTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private static readonly MetadataRecordDto Inception = new()
    {
        Kind = "Film",
        Title = "Inception",
        Creators = ["Christopher Nolan"],
        Year = 2010,
        DurationMinutes = 148,
        Genre = "Action, Adventure, Sci-Fi",
        Actors = ["Leonardo DiCaprio", "Joseph Gordon-Levitt", "Elliot Page"],
        Source = "omdb"
    };

    [Fact]
    public async Task La_disponibilite_reflete_celle_de_Metadata_Api()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        fixture.Metadata.FilmSearchEnabled = false;
        Assert.False((await client.GetFromJsonAsync<FilmAvailabilityResponse>("/films/availability", CatalogueApiFixture.Json))!.Enabled);

        fixture.Metadata.FilmSearchEnabled = true;
        Assert.True((await client.GetFromJsonAsync<FilmAvailabilityResponse>("/films/availability", CatalogueApiFixture.Json))!.Enabled);
    }

    [Fact]
    public async Task Un_film_choisi_donne_une_fiche_pre_remplie_qui_s_enregistre_telle_quelle()
    {
        fixture.Metadata.FilmAnswer = _ => new MetadataLookup(MetadataLookupOutcome.Found, Inception);
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var result = await client.GetFromJsonAsync<FilmDraftResponse>("/films/tt1375666", CatalogueApiFixture.Json);

        var draft = result!.Draft;
        Assert.Equal(MediaType.Film, draft.Type);
        Assert.Equal("Inception", draft.Title);
        Assert.Equal("Christopher Nolan", draft.Creator);
        Assert.Equal(2010, draft.Year);
        var film = Assert.IsType<FilmSpecifics>(draft.Specifics);
        Assert.Equal(148, film.DurationMinutes);
        Assert.Equal("Action, Adventure, Sci-Fi", film.Genre);
        Assert.Equal(["Leonardo DiCaprio", "Joseph Gordon-Levitt", "Elliot Page"], film.Actors);
        Assert.Equal("omdb", result.Source);

        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            draft.Type, draft.Title, draft.Creator, draft.Year, "Salon, meuble TV", draft.Specifics),
            CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
    }

    [Fact]
    public async Task Une_recherche_indisponible_est_distinguee_d_une_recherche_sans_resultat()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        fixture.Metadata.FilmSearchAnswer = _ => new FilmSearch(MetadataLookupOutcome.Found, []);
        var vide = await client.GetFromJsonAsync<FilmSearchResponse>("/films?title=Introuvable", CatalogueApiFixture.Json);
        Assert.Empty(vide!.Films);
        Assert.False(vide.Unavailable);

        fixture.Metadata.FilmSearchAnswer = _ => FilmSearch.Unavailable;
        var panne = await client.GetFromJsonAsync<FilmSearchResponse>("/films?title=Inception", CatalogueApiFixture.Json);
        Assert.Empty(panne!.Films);
        Assert.True(panne.Unavailable);
    }

    [Fact]
    public async Task Une_fiche_indisponible_donne_un_503()
    {
        fixture.Metadata.FilmAnswer = _ => MetadataLookup.Unavailable;
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/films/tt1375666")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public async Task Un_titre_trop_court_est_refuse(string title)
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/films?title={title}")).StatusCode);
    }

    [Fact]
    public async Task La_recherche_de_films_exige_un_menage()
    {
        var client = fixture.ClientWithToken(TestTokens.WithoutHousehold(Roles.Adult));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/films?title=Inception")).StatusCode);
    }

    [Fact]
    public void Un_acteur_vide_est_refuse_a_l_entree()
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

        var exception = Assert.Throws<MediaValidationException>(() => Media.Create(
            MediaType.Film, "Inception", null, null, null, new FilmSpecifics { Actors = ["Leonardo DiCaprio", " "] }, now));

        Assert.Contains("Un acteur ne peut pas être vide.", exception.Errors);
    }
}
