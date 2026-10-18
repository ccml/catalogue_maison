using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Infrastructure;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Tri et pagination de la liste. Chaque test marque ses fiches d'un mot
/// unique et filtre la liste dessus : le ménage de test est partagé avec les
/// autres classes.
/// </summary>
public sealed class MediaListTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private readonly HttpClient _client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

    [Fact]
    public async Task Le_tri_par_serie_range_les_albums_par_tome_puis_les_fiches_sans_serie()
    {
        var marque = Marque();

        // Saisis dans le désordre : c'est le tome qui doit ranger, pas
        // l'ordre d'ajout ni le titre (« Tome 10 » passerait avant « Tome 2 »).
        await CreeBd($"Tome 10 {marque}", marque, 10);
        await CreeBd($"Tome 2 {marque}", marque, 2);
        await CreeLivre($"Aaa sans série {marque}");
        await CreeBd($"Tome 1 {marque}", marque, 1);

        var liste = await Liste($"q={marque}&sort=Series");

        Assert.Equal(
            [$"Tome 1 {marque}", $"Tome 2 {marque}", $"Tome 10 {marque}", $"Aaa sans série {marque}"],
            liste.Items.Select(m => m.Title));
    }

    [Fact]
    public async Task Le_tri_par_titre_suit_l_alphabet()
    {
        var marque = Marque();

        await CreeLivre($"{marque} Cerise");
        await CreeLivre($"{marque} Abricot");
        await CreeLivre($"{marque} Banane");

        var liste = await Liste($"q={marque}&sort=Title");

        Assert.Equal(
            [$"{marque} Abricot", $"{marque} Banane", $"{marque} Cerise"],
            liste.Items.Select(m => m.Title));
    }

    [Fact]
    public async Task Le_tri_par_auteur_met_les_fiches_sans_auteur_a_la_fin()
    {
        var marque = Marque();

        await CreeLivre($"{marque} anonyme");
        await CreeLivre($"{marque} de Hugo", "Victor Hugo");
        await CreeLivre($"{marque} de Zola", "Émile Zola");

        var liste = await Liste($"q={marque}&sort=Creator");

        // « Émile » se range avec les E, et non après le Z comme le ferait
        // un tri par octets : la collation de la base doit rester linguistique.
        Assert.Equal(
            [$"{marque} de Zola", $"{marque} de Hugo", $"{marque} anonyme"],
            liste.Items.Select(m => m.Title));
    }

    [Fact]
    public async Task Les_pages_successives_couvrent_toute_la_liste_sans_doublon()
    {
        var marque = Marque();
        for (var i = 1; i <= 5; i++)
        {
            await CreeLivre($"{marque} {i}");
        }

        var premiere = await Liste($"q={marque}&size=2&page=1");
        var deuxieme = await Liste($"q={marque}&size=2&page=2");
        var troisieme = await Liste($"q={marque}&size=2&page=3");

        Assert.Equal(5, premiere.Total);
        var titres = premiere.Items.Concat(deuxieme.Items).Concat(troisieme.Items).Select(m => m.Title).ToList();
        Assert.Equal(5, titres.Distinct().Count());
        Assert.Single(troisieme.Items);
    }

    private static string Marque() => $"tri{Guid.NewGuid():N}"[..14];

    private async Task<PagedResponse<MediaSummaryResponse>> Liste(string query)
    {
        var liste = await _client.GetFromJsonAsync<PagedResponse<MediaSummaryResponse>>(
            $"/medias?{query}", CatalogueApiFixture.Json);
        Assert.NotNull(liste);
        return liste;
    }

    private Task CreeLivre(string titre, string? auteur = null) =>
        Cree(new CreateMediaRequest(MediaType.Book, titre, auteur));

    private Task CreeBd(string titre, string serie, int tome) =>
        Cree(new CreateMediaRequest(
            MediaType.ComicBook, titre, Specifics: new ComicBookSpecifics { Series = serie, Volume = tome }));

    private async Task Cree(CreateMediaRequest requete)
    {
        var reponse = await _client.PostAsJsonAsync("/medias", requete, CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
    }
}
