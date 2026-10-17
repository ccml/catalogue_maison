using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Features.Scans;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Metadata;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Chemin « scan » : doublon dans le ménage, puis fiche pré-remplie. Et
/// surtout, l'échec de l'enrichissement n'empêche jamais l'ajout.
/// </summary>
public sealed class ScanTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private static readonly MetadataRecordDto Blacksad = new()
    {
        Kind = "ComicBook",
        Title = "Ame rouge",
        Creators = ["Juan Díaz Canales"],
        Year = 2006,
        Publisher = "Dargaud",
        Pages = 100,
        Isbn = "9782205059250",
        Series = "Blacksad",
        Volume = 3,
        Illustrator = "Juanjo Guarnido",
        Source = "bnf"
    };

    [Fact]
    public async Task Une_notice_trouvee_donne_une_fiche_pre_remplie_et_n_enregistre_rien()
    {
        fixture.Metadata.Answer = _ => new MetadataLookup(MetadataLookupOutcome.Found, Blacksad);
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var scan = await client.GetFromJsonAsync<ScanResponse>("/scans/978-2-205-05925-0", CatalogueApiFixture.Json);

        Assert.NotNull(scan);
        Assert.Equal("9782205059250", scan.Code);
        Assert.Equal(EnrichmentStatus.Found, scan.Enrichment);
        Assert.Equal("bnf", scan.Source);
        Assert.Null(scan.Existing);

        var draft = scan.Draft;
        Assert.NotNull(draft);
        Assert.Equal(MediaType.ComicBook, draft.Type);
        Assert.Equal("Ame rouge", draft.Title);
        Assert.Equal("Juan Díaz Canales", draft.Creator);
        var comic = Assert.IsType<ComicBookSpecifics>(draft.Specifics);
        Assert.Equal("Blacksad", comic.Series);
        Assert.Equal(3, comic.Volume);
        Assert.Equal("Juanjo Guarnido", comic.Illustrator);
        Assert.Equal("9782205059250", comic.Isbn);

        // Pré-remplir n'est pas enregistrer : l'utilisateur valide d'abord.
        var catalogue = await client.GetFromJsonAsync<PagedResponse<MediaSummaryResponse>>(
            "/medias?q=Blacksad", CatalogueApiFixture.Json);
        Assert.Empty(catalogue!.Items);
    }

    [Fact]
    public async Task La_fiche_proposee_s_enregistre_telle_quelle()
    {
        // La fiche renvoyée par le scan doit être une fiche que l'API
        // accepte : sinon l'utilisateur qui valide sans rien toucher
        // recevrait un refus.
        fixture.Metadata.Answer = _ => new MetadataLookup(MetadataLookupOutcome.Found, Blacksad);
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdB, Roles.Adult);

        var draft = (await client.GetFromJsonAsync<ScanResponse>("/scans/9782205059250", CatalogueApiFixture.Json))!.Draft!;
        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            draft.Type, draft.Title, draft.Creator, draft.Year, "Bureau", draft.Specifics), CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        // Rescanné, l'album est désormais reconnu comme déjà possédé, et
        // Metadata.Api n'est plus sollicité.
        var before = fixture.Metadata.Requested.Count;
        var rescan = await client.GetFromJsonAsync<ScanResponse>("/scans/9782205059250", CatalogueApiFixture.Json);

        Assert.Equal(EnrichmentStatus.NotAttempted, rescan!.Enrichment);
        Assert.Equal("Ame rouge", rescan.Existing?.Title);
        Assert.Null(rescan.Draft);
        Assert.Equal(before, fixture.Metadata.Requested.Count);
    }

    [Fact]
    public async Task Un_livre_saisi_avec_son_ISBN_10_est_reconnu_au_scan_de_son_EAN_13()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Book, "Le facteur fatal", Specifics: new BookSpecifics { Isbn = "2-07-040827-2" }),
            CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        var scan = await client.GetFromJsonAsync<ScanResponse>("/scans/9782070408276", CatalogueApiFixture.Json);

        Assert.Equal("Le facteur fatal", scan!.Existing?.Title);
        Assert.DoesNotContain("9782070408276", fixture.Metadata.Requested);
    }

    [Fact]
    public async Task Un_enrichissement_indisponible_n_empeche_pas_l_ajout()
    {
        fixture.Metadata.Answer = _ => MetadataLookup.Unavailable;
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var scan = await client.GetAsync("/scans/9791090636071");

        // Pas d'erreur : un 200 qui dit l'indisponibilité, et une fiche qui
        // garde au moins l'ISBN scanné.
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        var body = await scan.Content.ReadFromJsonAsync<ScanResponse>(CatalogueApiFixture.Json);
        Assert.Equal(EnrichmentStatus.Unavailable, body!.Enrichment);
        Assert.Equal(MediaType.Book, body.Draft?.Type);
        Assert.Equal("9791090636071", Assert.IsType<BookSpecifics>(body.Draft?.Specifics).Isbn);
    }

    [Fact]
    public async Task Un_code_barres_inconnu_hors_livre_ne_propose_aucune_fiche()
    {
        fixture.Metadata.Answer = _ => MetadataLookup.NotFound;
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var scan = await client.GetFromJsonAsync<ScanResponse>("/scans/3700000000001", CatalogueApiFixture.Json);

        Assert.Equal(EnrichmentStatus.NotFound, scan!.Enrichment);
        Assert.Null(scan.Draft);
        Assert.Null(scan.Existing);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    public async Task Un_code_mal_forme_est_refuse(string code)
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var scan = await client.GetAsync($"/scans/{code}");

        Assert.Equal(HttpStatusCode.BadRequest, scan.StatusCode);
    }

    [Fact]
    public async Task Le_scan_exige_un_menage()
    {
        var client = fixture.ClientWithToken(TestTokens.WithoutHousehold(Roles.Adult));

        var scan = await client.GetAsync("/scans/9782205059250");

        Assert.Equal(HttpStatusCode.Unauthorized, scan.StatusCode);
    }
}
