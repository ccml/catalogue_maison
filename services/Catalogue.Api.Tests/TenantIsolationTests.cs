using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Infrastructure;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Le garde-fou du modèle multitenant. Si un seul de ces tests passe au
/// rouge, l'isolation entre ménages est rompue — ne jamais le neutraliser
/// pour faire passer une livraison.
/// </summary>
public sealed class TenantIsolationTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private const string Isbn = "978-2-07-040827-8";
    private const string IsbnNormalise = "9782070408278";

    [Fact]
    public async Task Un_menage_ne_voit_rien_du_catalogue_d_un_autre()
    {
        // Le ménage A enregistre un livre, ISBN compris.
        var menageA = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var creation = await menageA.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Book,
            "Le Horla",
            "Guy de Maupassant",
            1887,
            "Salon, étagère du haut",
            new BookSpecifics { Isbn = Isbn, Publisher = "Gallimard" }),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        var livre = await creation.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);
        Assert.NotNull(livre);

        // Le ménage B ne doit l'atteindre par aucun chemin.
        var menageB = fixture.ClientFor(CatalogueApiFixture.HouseholdB, Roles.Adult);

        // 1. Par recherche.
        var recherche = await menageB.GetFromJsonAsync<PagedResponse<MediaSummaryResponse>>(
            "/medias?q=Horla", CatalogueApiFixture.Json);
        Assert.NotNull(recherche);
        Assert.Empty(recherche.Items);
        Assert.Equal(0, recherche.Total);

        // 2. Par accès direct à l'identifiant.
        var accesDirect = await menageB.GetAsync($"/medias/{livre!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, accesDirect.StatusCode);

        // 3. Par scan d'un code-barres déjà présent chez l'autre ménage.
        var scan = await menageB.GetAsync($"/medias/by-isbn/{IsbnNormalise}");
        Assert.Equal(HttpStatusCode.NotFound, scan.StatusCode);

        // 4. Ni en écriture : l'isolation doit couvrir la correction et la
        // suppression, pas seulement la lecture.
        var correction = await menageB.PutAsJsonAsync($"/medias/{livre!.Id}", new UpdateMediaRequest(
            MediaType.Book, "Titre imposé depuis un autre ménage"), CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.NotFound, correction.StatusCode);

        var suppression = await menageB.DeleteAsync($"/medias/{livre.Id}");
        Assert.Equal(HttpStatusCode.NotFound, suppression.StatusCode);

        var prete = await menageB.PutAsJsonAsync($"/medias/{livre.Id}/lent", new SetLentRequest(true),
            CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.NotFound, prete.StatusCode);

        // Contrôle : les trois mêmes chemins aboutissent chez le ménage A,
        // sinon le test ne prouverait que l'absence de données.
        var rechercheA = await menageA.GetFromJsonAsync<PagedResponse<MediaSummaryResponse>>(
            "/medias?q=Horla", CatalogueApiFixture.Json);
        Assert.NotNull(rechercheA);
        Assert.Single(rechercheA.Items);

        var accesDirectA = await menageA.GetFromJsonAsync<MediaDetailResponse>(
            $"/medias/{livre.Id}", CatalogueApiFixture.Json);
        Assert.NotNull(accesDirectA);
        Assert.Equal("Le Horla", accesDirectA!.Title);
        Assert.False(accesDirectA.IsLent);

        var scanA = await menageA.GetAsync($"/medias/by-isbn/{IsbnNormalise}");
        Assert.Equal(HttpStatusCode.OK, scanA.StatusCode);
    }

    [Fact]
    public async Task Un_jeton_sans_claim_tenant_est_rejete()
    {
        var client = fixture.ClientWithToken(TestTokens.WithoutHousehold(Roles.Adult));

        var reponse = await client.GetAsync("/medias");

        // Jamais de ménage par défaut, jamais de repli.
        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("information_schema")]
    [InlineData("pg_catalog")]
    [InlineData("martin; drop table medias")]
    [InlineData("MARTIN")]
    [InlineData("")]
    public async Task Un_claim_tenant_malforme_est_rejete(string tenant)
    {
        var client = fixture.ClientWithToken(TestTokens.WithMalformedHousehold(tenant, Roles.Adult));

        var reponse = await client.GetAsync("/medias");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_health_check_reste_anonyme()
    {
        var client = fixture.CreateClient();

        var reponse = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }
}
