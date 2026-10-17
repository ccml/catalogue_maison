using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metadata.Api.Domain;
using Metadata.Api.Infrastructure.Persistence;
using Metadata.Api.Infrastructure.Sources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metadata.Api.Tests;

/// <summary>
/// Lecture des réponses d'OMDb. Les fixtures suivent la forme documentée des
/// réponses d'OMDb (aucune clé n'était disponible pour les relever) : à
/// remplacer par des réponses réelles dès qu'une clé l'est.
/// </summary>
public sealed class OmdbParsingTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Une_recherche_ne_propose_que_des_films_et_des_series()
    {
        var films = OmdbClient.ParseSearch(Fixture("omdb-recherche-inception.json"));

        Assert.Equal(["tt1375666", "tt5295894", "tt4034354"], films.Select(f => f.Id));
        Assert.Equal("Inception", films[0].Title);
        Assert.Equal("2010", films[0].Year);
    }

    [Fact]
    public void Une_recherche_sans_resultat_ne_propose_rien()
    {
        Assert.Empty(OmdbClient.ParseSearch(Fixture("omdb-introuvable.json")));
    }

    [Fact]
    public void La_fiche_donne_realisateur_duree_genre_et_acteurs()
    {
        var film = OmdbClient.ParseFilm(Fixture("omdb-fiche-inception.json"));

        Assert.NotNull(film);
        Assert.Equal(MetadataKind.Film, film.Kind);
        Assert.Equal("Inception", film.Title);
        Assert.Equal(2010, film.Year);
        Assert.Equal(["Christopher Nolan"], film.Creators);
        Assert.Equal(148, film.DurationMinutes);
        Assert.Equal("Action, Adventure, Sci-Fi", film.Genre);
        Assert.Equal(["Leonardo DiCaprio", "Joseph Gordon-Levitt", "Elliot Page"], film.Actors);
        Assert.Equal(OmdbClient.SourceName, film.Source);
    }

    [Fact]
    public void Les_valeurs_N_A_sont_des_absences()
    {
        // OMDb écrit « N/A » plutôt que d'omettre un champ : ce ne doit
        // jamais devenir un réalisateur ou un genre.
        var film = OmdbClient.ParseFilm(Fixture("omdb-fiche-incomplete.json"));

        Assert.NotNull(film);
        Assert.Empty(film.Creators);
        Assert.Empty(film.Actors);
        Assert.Null(film.Genre);
        Assert.Null(film.DurationMinutes);
        Assert.Equal(1978, film.Year);
    }

    [Theory]
    [InlineData("omdb-cle-refusee.json")]
    [InlineData("omdb-quota.json")]
    public void Une_cle_refusee_ou_un_quota_atteint_est_une_panne_pas_un_film_inconnu(string fixture)
    {
        var exception = Assert.Throws<MetadataSourceException>(() => OmdbClient.ParseSearch(Fixture(fixture)));
        Assert.Equal(OmdbClient.SourceName, exception.SourceName);
    }

    [Theory]
    [InlineData("tt1375666", true)]
    [InlineData(" TT1375666 ", true)]
    [InlineData("tt123", false)]
    [InlineData("1375666", false)]
    [InlineData("tt1375666&apikey=autre", false)]
    public void L_identifiant_IMDb_est_valide_strictement(string raw, bool valid)
    {
        Assert.Equal(valid, FilmId.TryParse(raw, out _));
    }
}

/// <summary>Recherche de films : désactivée sans clé, mise en cache avec.</summary>
[Collection(MetadataApiCollection.Name)]
public sealed class FilmEndpointTests(MetadataApiFixture fixture) : IAsyncLifetime
{
    private const string Key = "cle-de-test-ne-doit-jamais-sortir";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly StubOmdb _omdb = new();

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MetadataDbContext>().Films.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sans_cle_la_recherche_de_films_est_desactivee()
    {
        var client = fixture.ServiceClient();

        var availability = await client.GetFromJsonAsync<JsonElement>("/films/availability");
        Assert.False(availability.GetProperty("enabled").GetBoolean());

        Assert.Equal(HttpStatusCode.NotImplemented, (await client.GetAsync("/films?title=Inception")).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await client.GetAsync("/films/tt1375666")).StatusCode);
    }

    [Fact]
    public async Task Avec_une_cle_un_film_se_cherche_puis_sa_fiche_est_mise_en_cache()
    {
        var client = WithKey();

        var availability = await client.GetFromJsonAsync<JsonElement>("/films/availability");
        Assert.True(availability.GetProperty("enabled").GetBoolean());

        var films = await client.GetFromJsonAsync<List<FilmCandidate>>("/films?title=Inception", Json);
        Assert.Equal("tt1375666", films![0].Id);

        var first = await client.GetFromJsonAsync<MetadataRecord>("/films/tt1375666", Json);
        var second = await client.GetFromJsonAsync<MetadataRecord>("/films/tt1375666", Json);

        Assert.Equal(148, first!.DurationMinutes);
        Assert.Equal(first.Title, second!.Title);

        // Une recherche, puis une seule lecture de fiche : la seconde vient du cache.
        Assert.Equal(1, _omdb.Requests.Count(r => r.Contains("&i=", StringComparison.Ordinal)));
        Assert.All(_omdb.Requests, r => Assert.Contains($"apikey={Key}", r));
    }

    [Fact]
    public async Task Une_cle_refusee_donne_un_503_qui_ne_revele_pas_la_cle()
    {
        _omdb.Body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "omdb-cle-refusee.json"));
        var client = WithKey();

        var response = await client.GetAsync("/films?title=Inception");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(Key, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_identifiant_mal_forme_n_atteint_jamais_OMDb()
    {
        var response = await WithKey().GetAsync("/films/tt1375666%26s%3Dautre");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_omdb.Requests);
    }

    private HttpClient WithKey()
    {
        var factory = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Enrichment:OmdbApiKey", Key);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<OmdbClient>().ConfigurePrimaryHttpMessageHandler(() => _omdb));
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", MetadataApiFixture.Token(MetadataApiFixture.Audience, ["enrichissement"]));
        return client;
    }

    /// <summary>OMDb simulé : répond selon le paramètre, et garde la trace des URL appelées.</summary>
    private sealed class StubOmdb : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        /// <summary>Corps imposé à toutes les réponses ; sinon, selon la requête.</summary>
        public string? Body { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            Requests.Add(uri);

            var fixture = uri.Contains("&s=", StringComparison.Ordinal) ? "omdb-recherche-inception.json" : "omdb-fiche-inception.json";
            var body = Body ?? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
