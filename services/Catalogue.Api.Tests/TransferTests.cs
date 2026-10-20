using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Features.Transfer;
using Catalogue.Api.Infrastructure;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Export et import du catalogue. Chaque test travaille dans des ménages
/// neufs : les autres classes de tests écrivent dans les ménages communs du
/// fixture, et un import se juge sur un catalogue dont on connaît tout.
/// </summary>
public sealed class TransferTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private static readonly DateTimeOffset AjoutAncien = new(2019, 3, 2, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Un_export_reimporte_ailleurs_redonne_le_meme_catalogue()
    {
        var source = await NouveauMenageAsync("transfert_source");
        var cible = await NouveauMenageAsync("transfert_cible");

        await CreerAsync(source, new CreateMediaRequest(MediaType.Book, "Le Horla", "Guy de Maupassant", 1887,
            "Salon", new BookSpecifics { Isbn = "978-2-07-040827-8", Publisher = "Gallimard" }));
        var disque = await CreerAsync(source, new CreateMediaRequest(MediaType.Music, "Kind of Blue", "Miles Davis",
            1959, null, new MusicSpecifics { Label = "Columbia", Tracks = ["So What", "Blue in Green"] }));
        await CreerAsync(source, new CreateMediaRequest(MediaType.Game, "Carcassonne"));
        (await source.PutAsJsonAsync($"/medias/{disque.Id}/lent", new SetLentRequest(true),
            CatalogueApiFixture.Json)).EnsureSuccessStatusCode();

        var export = await ExporterAsync(source);
        Assert.Equal(CatalogueExport.FormatName, export.Format);
        Assert.Equal(CatalogueExport.CurrentVersion, export.Version);
        Assert.Equal(3, export.Medias.Count);

        var bilan = await ImporterAsync(cible, export);
        Assert.Equal(3, bilan.Imported);
        Assert.Empty(bilan.Skipped);

        var avant = await DetailsAsync(source);
        var apres = await DetailsAsync(cible);

        // Tout est identique, sauf l'identifiant : spécificités polymorphes,
        // date d'ajout et prêt compris.
        Assert.Equal(
            avant.Select(m => m with { Id = Guid.Empty }).OrderBy(m => m.Title),
            apres.Select(m => m with { Id = Guid.Empty }).OrderBy(m => m.Title),
            new DetailComparer());
        Assert.Empty(avant.Select(m => m.Id).Intersect(apres.Select(m => m.Id)));

        var kindOfBlue = apres.Single(m => m.Title == "Kind of Blue");
        Assert.True(kindOfBlue.IsLent);
        Assert.Equal(["So What", "Blue in Green"], Assert.IsType<MusicSpecifics>(kindOfBlue.Specifics).Tracks);
    }

    [Fact]
    public async Task L_export_ne_porte_ni_identifiant_de_fiche_ni_menage()
    {
        const string menage = "transfert_discret";
        var client = await NouveauMenageAsync(menage);
        var livre = await CreerAsync(client, new CreateMediaRequest(MediaType.Book, "Le Horla"));

        var brut = await client.GetStringAsync("/medias/export");

        Assert.DoesNotContain(livre.Id.ToString(), brut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(menage, brut, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(brut);
        var fiche = document.RootElement.GetProperty("medias")[0];
        Assert.False(fiche.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Reimporter_son_propre_export_n_ajoute_rien()
    {
        var client = await NouveauMenageAsync("transfert_double");
        await CreerAsync(client, new CreateMediaRequest(MediaType.Book, "Le Horla", null, null, null,
            new BookSpecifics { Isbn = "9782070408278" }));
        await CreerAsync(client, new CreateMediaRequest(MediaType.Film, "Alien", "Ridley Scott"));

        var bilan = await ImporterAsync(client, await ExporterAsync(client));

        Assert.Equal(0, bilan.Imported);
        Assert.Equal(
            [DuplicateReason.SameIsbn, DuplicateReason.SameTitle],
            bilan.Skipped.OrderBy(s => s.Position).Select(s => s.Reason));
        Assert.Equal(2, (await DetailsAsync(client)).Count);
    }

    [Fact]
    public async Task L_import_ajoute_les_nouveautes_et_saute_les_doublons()
    {
        var client = await NouveauMenageAsync("transfert_fusion");
        await CreerAsync(client, new CreateMediaRequest(MediaType.Book, "Le Horla", null, null, null,
            new BookSpecifics { Isbn = "2070408272" }));

        var bilan = await ImporterAsync(client, Fichier(
            new ExportedMedia(MediaType.Book, "Le Horla (poche)", Specifics: new BookSpecifics { Isbn = "9782070408276" }),
            new ExportedMedia(MediaType.Film, "Alien", AddedAt: AjoutAncien),
            new ExportedMedia(MediaType.Film, "alien ")));

        Assert.Equal(1, bilan.Imported);
        Assert.Equal(
            [new SkippedMedia(1, MediaType.Book, "Le Horla (poche)", DuplicateReason.SameIsbn),
             new SkippedMedia(3, MediaType.Film, "alien", DuplicateReason.RepeatedInFile)],
            bilan.Skipped);

        var alien = (await DetailsAsync(client)).Single(m => m.Type == MediaType.Film);
        Assert.Equal(AjoutAncien, alien.AddedAt);
    }

    [Fact]
    public async Task Une_seule_fiche_invalide_fait_refuser_tout_le_fichier()
    {
        var client = await NouveauMenageAsync("transfert_invalide");

        var reponse = await client.PostAsJsonAsync("/medias/import", Fichier(
            new ExportedMedia(MediaType.Film, "Alien"),
            new ExportedMedia(MediaType.Book, "   "),
            new ExportedMedia(MediaType.Book, "Dune", Specifics: new FilmSpecifics { Studio = "Legendary" })),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var probleme = await reponse.Content.ReadFromJsonAsync<ProblemeDeValidation>(CatalogueApiFixture.Json);
        var erreurs = probleme!.Errors["medias"];
        Assert.Contains(erreurs, e => e.StartsWith("Fiche 2 : ", StringComparison.Ordinal));
        Assert.Contains(erreurs, e => e.StartsWith("Fiche 3 (Dune) : ", StringComparison.Ordinal));

        // Rien n'a été écrit, pas même la fiche valide.
        Assert.Empty(await DetailsAsync(client));
    }

    [Theory]
    [InlineData("autre-application", CatalogueExport.CurrentVersion)]
    [InlineData(CatalogueExport.FormatName, 99)]
    public async Task Un_fichier_d_un_autre_format_ou_version_est_refuse(string format, int version)
    {
        var client = await NouveauMenageAsync("transfert_format");

        var reponse = await client.PostAsJsonAsync("/medias/import",
            new CatalogueExport(format, version, AjoutAncien, [new ExportedMedia(MediaType.Film, "Alien")]),
            CatalogueApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Empty(await DetailsAsync(client));
    }

    [Fact]
    public async Task Un_enfant_ne_peut_ni_exporter_ni_importer()
    {
        const string menage = "transfert_enfant";
        await NouveauMenageAsync(menage);
        var enfant = fixture.ClientFor(menage, Roles.Child);

        Assert.Equal(HttpStatusCode.Forbidden, (await enfant.GetAsync("/medias/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await enfant.PostAsJsonAsync("/medias/import",
            Fichier(new ExportedMedia(MediaType.Film, "Alien")), CatalogueApiFixture.Json)).StatusCode);
    }

    [Fact]
    public async Task L_export_et_l_import_restent_dans_le_menage_du_jeton()
    {
        var menageA = await NouveauMenageAsync("transfert_isole_a");
        var menageB = await NouveauMenageAsync("transfert_isole_b");
        await CreerAsync(menageB, new CreateMediaRequest(MediaType.Book, "Livre de B"));

        // L'export de A ne contient rien de B…
        Assert.Empty((await ExporterAsync(menageA)).Medias);

        // … et un import par A n'écrit que chez A, ni ne prend pour doublon
        // une fiche que seul B possède.
        var bilan = await ImporterAsync(menageA, Fichier(new ExportedMedia(MediaType.Book, "Livre de B")));
        Assert.Equal(1, bilan.Imported);
        Assert.Single(await DetailsAsync(menageB));
        Assert.Single(await DetailsAsync(menageA));
    }

    private async Task<HttpClient> NouveauMenageAsync(string menage)
    {
        await fixture.ProvisionAsync(menage);
        return fixture.ClientFor(menage, Roles.Adult);
    }

    private static CatalogueExport Fichier(params ExportedMedia[] medias) =>
        new(CatalogueExport.FormatName, CatalogueExport.CurrentVersion, AjoutAncien, medias);

    private static async Task<MediaDetailResponse> CreerAsync(HttpClient client, CreateMediaRequest request)
    {
        var reponse = await client.PostAsJsonAsync("/medias", request, CatalogueApiFixture.Json);
        reponse.EnsureSuccessStatusCode();
        return (await reponse.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json))!;
    }

    private static async Task<CatalogueExport> ExporterAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CatalogueExport>("/medias/export", CatalogueApiFixture.Json))!;

    private static async Task<ImportReport> ImporterAsync(HttpClient client, CatalogueExport fichier)
    {
        var reponse = await client.PostAsJsonAsync("/medias/import", fichier, CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<ImportReport>(CatalogueApiFixture.Json))!;
    }

    private static async Task<IReadOnlyList<MediaDetailResponse>> DetailsAsync(HttpClient client)
    {
        var liste = await client.GetFromJsonAsync<PagedResponse<MediaSummaryResponse>>(
            "/medias?size=200", CatalogueApiFixture.Json);

        var details = new List<MediaDetailResponse>();
        foreach (var resume in liste!.Items)
        {
            details.Add((await client.GetFromJsonAsync<MediaDetailResponse>(
                $"/medias/{resume.Id}", CatalogueApiFixture.Json))!);
        }

        return details;
    }

    private sealed record ProblemeDeValidation(Dictionary<string, string[]> Errors);

    /// <summary>
    /// Compare deux fiches champ à champ. L'égalité des records ne suffit
    /// pas : les listes des spécificités (pistes, acteurs) se comparent par
    /// référence.
    /// </summary>
    private sealed class DetailComparer : IEqualityComparer<MediaDetailResponse>
    {
        public bool Equals(MediaDetailResponse? x, MediaDetailResponse? y) =>
            JsonSerializer.Serialize(x, CatalogueApiFixture.Json) == JsonSerializer.Serialize(y, CatalogueApiFixture.Json);

        public int GetHashCode(MediaDetailResponse obj) => obj.Title.GetHashCode(StringComparison.Ordinal);
    }
}
