using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metadata.Api.Domain;
using Metadata.Api.Features.Lookups;
using Metadata.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metadata.Api.Tests;

[CollectionDefinition(Name)]
public sealed class MetadataApiCollection : ICollectionFixture<MetadataApiFixture>
{
    public const string Name = "Metadata.Api";
}

[Collection(MetadataApiCollection.Name)]
public sealed class LookupTests(MetadataApiFixture fixture) : IAsyncLifetime
{
    private const string Isbn = "9782205059250";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static MetadataRecord Blacksad(string source) => new()
    {
        Kind = MetadataKind.ComicBook,
        Title = "Ame rouge",
        Series = "Blacksad",
        Volume = 3,
        Isbn = Isbn,
        Source = source
    };

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Une_notice_trouvee_est_servie_puis_mise_en_cache()
    {
        fixture.Books.Answer = _ => Blacksad("livres");
        var client = fixture.ServiceClient();

        var first = await client.GetFromJsonAsync<LookupResponse>($"/lookups/{Isbn}", Json);
        var second = await client.GetFromJsonAsync<LookupResponse>("/lookups/2-205-05925-4", Json);

        Assert.Equal("Ame rouge", first!.Record.Title);
        Assert.Equal("Blacksad", first.Record.Series);

        // Même ouvrage sous sa forme ISBN-10 : servi par le cache, la source
        // n'est pas réinterrogée.
        Assert.Equal(first.Record.Title, second!.Record.Title);
        Assert.Equal(first.Record.Volume, second.Record.Volume);
        Assert.Equal(1, fixture.Books.Calls);
    }

    [Fact]
    public async Task La_source_de_repli_est_interrogee_quand_la_premiere_ne_connait_pas_le_code()
    {
        fixture.BooksFallback.Answer = _ => Blacksad("livres-repli");

        var response = await fixture.ServiceClient().GetFromJsonAsync<LookupResponse>($"/lookups/{Isbn}", Json);

        Assert.Equal("livres-repli", response!.Record.Source);
        Assert.Equal(1, fixture.Books.Calls);
        Assert.Equal(0, fixture.Music.Calls);
    }

    [Fact]
    public async Task Un_code_inconnu_de_toutes_les_sources_est_mis_en_cache_comme_tel()
    {
        var client = fixture.ServiceClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/lookups/{Isbn}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/lookups/{Isbn}")).StatusCode);

        Assert.Equal(1, fixture.Books.Calls);
        Assert.Equal(1, fixture.BooksFallback.Calls);
    }

    [Fact]
    public async Task Une_source_en_panne_donne_un_503_et_rien_n_est_mis_en_cache()
    {
        fixture.Books.Failing = true;
        var client = fixture.ServiceClient();

        var response = await client.GetAsync($"/lookups/{Isbn}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        // Une panne ne dit rien du code : il ne doit pas être retenu comme
        // inconnu. La source rétablie, la notice est trouvée.
        fixture.Books.Failing = false;
        fixture.Books.Answer = _ => Blacksad("livres");

        var retry = await client.GetFromJsonAsync<LookupResponse>($"/lookups/{Isbn}", Json);
        Assert.Equal("Ame rouge", retry!.Record.Title);
    }

    [Fact]
    public async Task Une_notice_perimee_est_servie_quand_les_sources_sont_en_panne()
    {
        fixture.Books.Answer = _ => Blacksad("livres");
        var client = fixture.ServiceClient();
        await client.GetAsync($"/lookups/{Isbn}");

        await AgeCacheAsync(TimeSpan.FromDays(400));
        fixture.Books.Failing = true;
        fixture.BooksFallback.Failing = true;

        var stale = await client.GetFromJsonAsync<LookupResponse>($"/lookups/{Isbn}", Json);

        Assert.Equal("Ame rouge", stale!.Record.Title);
        Assert.Equal(2, fixture.Books.Calls);
    }

    [Fact]
    public async Task Un_code_inconnu_est_redemande_une_fois_son_delai_ecoule()
    {
        var client = fixture.ServiceClient();
        await client.GetAsync($"/lookups/{Isbn}");

        await AgeCacheAsync(TimeSpan.FromDays(8));
        fixture.Books.Answer = _ => Blacksad("livres");

        var response = await client.GetFromJsonAsync<LookupResponse>($"/lookups/{Isbn}", Json);
        Assert.Equal("Ame rouge", response!.Record.Title);
    }

    [Fact]
    public async Task Une_notice_provisoire_est_redemandee_au_bout_d_une_semaine()
    {
        // Gardée 180 jours, elle masquerait la notice définitive — celle qui
        // dit qu'une BD est une BD — pendant six mois.
        fixture.Books.Answer = _ => Blacksad("livres") with { Provisional = true };
        var client = fixture.ServiceClient();
        await client.GetAsync($"/lookups/{Isbn}");

        await AgeCacheAsync(TimeSpan.FromDays(8));
        fixture.Books.Answer = _ => Blacksad("livres");

        var response = await client.GetFromJsonAsync<LookupResponse>($"/lookups/{Isbn}", Json);

        Assert.False(response!.Record.Provisional);
        Assert.Equal(2, fixture.Books.Calls);
    }

    [Fact]
    public async Task Une_notice_definitive_reste_en_cache_au_dela_d_une_semaine()
    {
        fixture.Books.Answer = _ => Blacksad("livres");
        var client = fixture.ServiceClient();
        await client.GetAsync($"/lookups/{Isbn}");

        await AgeCacheAsync(TimeSpan.FromDays(8));
        await client.GetAsync($"/lookups/{Isbn}");

        Assert.Equal(1, fixture.Books.Calls);
    }

    [Fact]
    public async Task Une_notice_ecrite_sous_une_forme_anterieure_est_redemandee()
    {
        // Une source qui apprend à lire un champ de plus ne doit pas laisser
        // servir pendant des mois des notices qui ne le portent pas.
        fixture.Books.Answer = _ => Blacksad("livres");
        var client = fixture.ServiceClient();
        await client.GetAsync($"/lookups/{Isbn}");

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
            await db.Lookups.ExecuteUpdateAsync(s => s.SetProperty(l => l.RecordVersion, 1));
        }

        await client.GetAsync($"/lookups/{Isbn}");

        Assert.Equal(2, fixture.Books.Calls);
    }

    [Fact]
    public async Task Un_code_barres_de_disque_n_est_pas_soumis_aux_sources_de_livres()
    {
        fixture.Music.Answer = code => new MetadataRecord
        {
            Kind = MetadataKind.Music,
            Title = "Homework",
            Creators = ["Daft Punk"],
            Source = "disques"
        };

        var response = await fixture.ServiceClient().GetFromJsonAsync<LookupResponse>("/lookups/0724384260958", Json);

        Assert.Equal(ProductCodeKind.Ean, response!.Kind);
        Assert.Equal("Homework", response.Record.Title);
        Assert.Equal(0, fixture.Books.Calls);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    public async Task Un_code_mal_forme_est_refuse_sans_interroger_les_sources(string code)
    {
        var response = await fixture.ServiceClient().GetAsync($"/lookups/{code}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fixture.Books.Calls + fixture.BooksFallback.Calls + fixture.Music.Calls);
    }

    [Fact]
    public async Task Le_cache_ne_retient_rien_du_menage_qui_a_cherche()
    {
        // Metadata.Api est tenant-agnostique : même si un jeton portait un
        // ménage, rien de lui ne doit finir dans le cache partagé.
        fixture.Books.Answer = _ => Blacksad("livres");
        var token = MetadataApiFixture.Token(
            MetadataApiFixture.Audience, ["enrichissement"], new() { ["tenant"] = "martin" });

        await fixture.ClientWithToken(token).GetAsync($"/lookups/{Isbn}");

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var raw = await db.Database
            .SqlQueryRaw<string>("select record::text as \"Value\" from lookups")
            .SingleAsync();

        Assert.DoesNotContain("martin", raw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task AgeCacheAsync(TimeSpan age)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var fetchedAt = DateTimeOffset.UtcNow - age;
        await db.Lookups.ExecuteUpdateAsync(s => s.SetProperty(l => l.FetchedAt, fetchedAt));
    }
}

[Collection(MetadataApiCollection.Name)]
public sealed class LookupAuthorizationTests(MetadataApiFixture fixture) : IAsyncLifetime
{
    private const string Uri = "/lookups/9782205059250";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sans_jeton_la_requete_est_refusee()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().GetAsync(Uri)).StatusCode);
    }

    [Fact]
    public async Task Un_jeton_d_utilisateur_destine_au_catalogue_est_refuse()
    {
        // Le jeton du frontend vise catalogue-api : il ne doit pas ouvrir
        // Metadata.Api, que seul Catalogue.Api interroge.
        var token = MetadataApiFixture.Token("catalogue-api", ["adulte"], new() { ["tenant"] = "martin" });

        var response = await fixture.ClientWithToken(token).GetAsync(Uri);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fixture.Books.Calls);
    }

    [Fact]
    public async Task Un_jeton_sans_le_role_enrichissement_est_interdit()
    {
        var token = MetadataApiFixture.Token(MetadataApiFixture.Audience, ["adulte"]);

        var response = await fixture.ClientWithToken(token).GetAsync(Uri);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, fixture.Books.Calls);
    }

    [Fact]
    public async Task La_sante_du_service_est_publique()
    {
        Assert.Equal(HttpStatusCode.OK, (await fixture.CreateClient().GetAsync("/health")).StatusCode);
    }
}
