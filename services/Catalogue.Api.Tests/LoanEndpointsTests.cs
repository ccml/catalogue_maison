using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Loans;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Les prêts vus du ménage prêteur. Les demandes venues d'un ménage
/// partenaire sont posées directement en base : c'est le chemin de partage
/// qui les crée, et il est testé à part.
/// </summary>
public sealed class LoanEndpointsTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private static readonly DateTimeOffset Hier = DateTimeOffset.UtcNow.AddDays(-1);

    private async Task<MediaDetailResponse> Enregistre(HttpClient client, string titre)
    {
        var reponse = await client.PostAsJsonAsync("/medias",
            new CreateMediaRequest(MediaType.Film, titre, Location: "Salon"), CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json))!;
    }

    /// <summary>Pose une demande de prêt, comme le ferait le chemin de partage.</summary>
    private async Task<Guid> Demande(string menage, Guid mediaId, string emprunteur)
    {
        await using var db = fixture.ContextFor(menage);
        var media = await db.Medias.SingleAsync(m => m.Id == mediaId);
        var demande = Loan.Request(media, Guid.NewGuid(), emprunteur, Hier);
        db.Loans.Add(demande);
        await db.SaveChangesAsync();
        return demande.Id;
    }

    private static async Task<IReadOnlyList<LoanResponse>> Prets(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<List<LoanResponse>>($"/loans{query}", CatalogueApiFixture.Json))!;

    [Fact]
    public async Task Un_pret_saisi_nomme_l_emprunteur_et_le_retour_le_clot()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var film = await Enregistre(client, "Solaris");

        var pret = await client.PutAsJsonAsync($"/medias/{film.Id}/lent",
            new SetLentRequest(true, "Tante Jeanne"), CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.NoContent, pret.StatusCode);

        var fiche = await client.GetFromJsonAsync<MediaDetailResponse>($"/medias/{film.Id}", CatalogueApiFixture.Json);
        Assert.True(fiche!.IsLent);
        Assert.Equal("Tante Jeanne", fiche.LentTo);

        var liste = await client.GetFromJsonAsync<PagedResponse<MediaSummaryResponse>>(
            "/medias?q=Solaris", CatalogueApiFixture.Json);
        Assert.Equal("Tante Jeanne", Assert.Single(liste!.Items).LentTo);

        var enCours = Assert.Single(await Prets(client, $"?mediaId={film.Id}"));
        Assert.Equal(LoanStatus.Active, enCours.Status);
        Assert.False(enCours.FromSharedHousehold);

        // Rejoué dans le même sens, l'appel ne crée pas de second prêt.
        var rejoue = await client.PutAsJsonAsync($"/medias/{film.Id}/lent",
            new SetLentRequest(true, "Oncle Paul"), CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.NoContent, rejoue.StatusCode);
        Assert.Single(await Prets(client, $"?mediaId={film.Id}"));

        var retour = await client.PostAsync($"/loans/{enCours.Id}/return", null);
        Assert.Equal(HttpStatusCode.NoContent, retour.StatusCode);

        fiche = await client.GetFromJsonAsync<MediaDetailResponse>($"/medias/{film.Id}", CatalogueApiFixture.Json);
        Assert.False(fiche!.IsLent);
        Assert.Null(fiche.LentTo);

        var rendu = Assert.Single(await Prets(client, $"?mediaId={film.Id}&status=Returned"));
        Assert.Equal("Tante Jeanne", rendu.BorrowerName);
        Assert.NotNull(rendu.ClosedAt);
    }

    [Fact]
    public async Task Accepter_une_demande_prete_le_media_et_refuse_les_autres()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var film = await Enregistre(client, "Le Miroir");

        var durand = await Demande(CatalogueApiFixture.HouseholdA, film.Id, "Famille Durand (paul)");
        var petit = await Demande(CatalogueApiFixture.HouseholdA, film.Id, "Famille Petit (lea)");

        var enAttente = await Prets(client, $"?mediaId={film.Id}");
        Assert.Equal(2, enAttente.Count);
        Assert.All(enAttente, p => Assert.True(p.FromSharedHousehold));
        Assert.All(enAttente, p => Assert.Equal("Le Miroir", p.MediaTitle));

        var acceptation = await client.PostAsync($"/loans/{durand}/accept", null);
        Assert.Equal(HttpStatusCode.NoContent, acceptation.StatusCode);

        var fiche = await client.GetFromJsonAsync<MediaDetailResponse>($"/medias/{film.Id}", CatalogueApiFixture.Json);
        Assert.True(fiche!.IsLent);
        Assert.Equal("Famille Durand (paul)", fiche.LentTo);

        var refusee = Assert.Single(await Prets(client, $"?mediaId={film.Id}&status=Declined"));
        Assert.Equal(petit, refusee.Id);

        // La demande refusée ne s'accepte plus.
        var tardive = await client.PostAsync($"/loans/{petit}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, tardive.StatusCode);
    }

    [Fact]
    public async Task Preter_directement_rend_les_demandes_sans_objet()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var film = await Enregistre(client, "Andreï Roublev");
        var demande = await Demande(CatalogueApiFixture.HouseholdA, film.Id, "Famille Durand (paul)");

        await client.PutAsJsonAsync($"/medias/{film.Id}/lent", new SetLentRequest(true, "Tante Jeanne"),
            CatalogueApiFixture.Json);

        var refusee = Assert.Single(await Prets(client, $"?mediaId={film.Id}&status=Declined"));
        Assert.Equal(demande, refusee.Id);
    }

    [Fact]
    public async Task Une_demande_se_refuse()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.HouseholdAdmin);
        var film = await Enregistre(fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult), "Nostalghia");
        var demande = await Demande(CatalogueApiFixture.HouseholdA, film.Id, "Famille Durand (paul)");

        var refus = await client.PostAsync($"/loans/{demande}/decline", null);
        Assert.Equal(HttpStatusCode.NoContent, refus.StatusCode);

        var fiche = await client.GetFromJsonAsync<MediaDetailResponse>($"/medias/{film.Id}", CatalogueApiFixture.Json);
        Assert.False(fiche!.IsLent);
        Assert.Empty(await Prets(client, $"?mediaId={film.Id}"));
    }

    [Fact]
    public async Task Un_enfant_voit_les_prets_mais_n_y_repond_pas()
    {
        var adulte = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var film = await Enregistre(adulte, "Le Sacrifice");
        var demande = await Demande(CatalogueApiFixture.HouseholdA, film.Id, "Famille Durand (paul)");

        var enfant = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Child);

        Assert.Single(await Prets(enfant, $"?mediaId={film.Id}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await enfant.PostAsync($"/loans/{demande}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await enfant.PostAsync($"/loans/{demande}/decline", null)).StatusCode);
    }

    [Fact]
    public async Task Un_menage_n_atteint_pas_les_prets_d_un_autre()
    {
        var menageA = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var film = await Enregistre(menageA, "L'Enfance d'Ivan");
        var demande = await Demande(CatalogueApiFixture.HouseholdA, film.Id, "Famille Petit (lea)");

        var menageB = fixture.ClientFor(CatalogueApiFixture.HouseholdB, Roles.Adult);

        Assert.DoesNotContain(await Prets(menageB), p => p.Id == demande);
        Assert.Empty(await Prets(menageB, $"?mediaId={film.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await menageB.PostAsync($"/loans/{demande}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await menageB.PostAsync($"/loans/{demande}/decline", null)).StatusCode);

        // La demande est intacte chez A.
        var intacte = Assert.Single(await Prets(menageA, $"?mediaId={film.Id}"));
        Assert.Equal(LoanStatus.Requested, intacte.Status);
    }

    [Fact]
    public async Task La_migration_dote_chaque_media_deja_prete_d_un_pret_en_cours()
    {
        const string menage = "avant_prets";
        var pretee = Guid.NewGuid();
        var presente = Guid.NewGuid();

        await using (var db = fixture.ContextFor(menage))
        {
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{menage}\"");

            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261018083957_PromeutLeTomeEnColonneGeneree");

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO medias (id, type, title, added_at, is_lent)
                VALUES ({0}, 'Film', 'Stalker', now(), true),
                       ({1}, 'Film', 'Solaris', now(), false)
                """, pretee, presente);

            await migrator.MigrateAsync();
        }

        await using (var db = fixture.ContextFor(menage))
        {
            var prets = await db.Loans.AsNoTracking().ToListAsync();

            var pret = Assert.Single(prets);
            Assert.Equal(pretee, pret.MediaId);
            Assert.Equal(LoanStatus.Active, pret.Status);
            Assert.Equal(Loan.UnknownBorrower, pret.BorrowerName);
        }
    }
}
