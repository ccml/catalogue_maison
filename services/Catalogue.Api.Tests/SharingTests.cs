using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Loans;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Features.Sharing;
using Catalogue.Api.Infrastructure;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Le second endroit, après le provisionnement, où le ménage vient de l'URL.
/// Il n'est ouvert qu'au compte de service de Tenants.Api — rôle de partage,
/// sans claim <c>tenant</c> —, et il ne rend qu'une vue restreinte.
/// </summary>
public sealed class SharingTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private static readonly Guid PartageDurand = Guid.NewGuid();
    private static readonly Guid PartagePetit = Guid.NewGuid();

    private HttpClient Service() => fixture.ClientWithToken(TestTokens.WithoutHousehold(Roles.Sharing));

    private static string Partage(Guid shareId, string menage = CatalogueApiFixture.HouseholdA) =>
        $"/internal/households/{menage}/shares/{shareId}";

    private async Task<MediaDetailResponse> EnregistreChezA(string titre, string? emplacement = "Bureau, étagère 3")
    {
        var reponse = await fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult).PostAsJsonAsync("/medias",
            new CreateMediaRequest(MediaType.Book, titre, "Ursula K. Le Guin", 1969, emplacement,
                new BookSpecifics { Publisher = "Ace Books" }),
            CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json))!;
    }

    private async Task<SharedMediaDetailResponse> Fiche(Guid shareId, Guid mediaId) =>
        (await Service().GetFromJsonAsync<SharedMediaDetailResponse>(
            $"{Partage(shareId)}/medias/{mediaId}", CatalogueApiFixture.Json))!;

    private async Task<HttpResponseMessage> Demande(Guid shareId, Guid mediaId, string emprunteur = "Famille Durand (bruno)") =>
        await Service().PostAsJsonAsync($"{Partage(shareId)}/medias/{mediaId}/loan-requests",
            new CreateLoanRequestRequest(emprunteur), CatalogueApiFixture.Json);

    [Fact]
    public async Task La_vue_partagee_ne_dit_ni_ou_ni_quand_ni_a_qui()
    {
        var livre = await EnregistreChezA("La Main gauche de la nuit");
        await fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult).PutAsJsonAsync(
            $"/medias/{livre.Id}/lent", new SetLentRequest(true, "Tante Jeanne"), CatalogueApiFixture.Json);

        var liste = await Service().GetAsync($"{Partage(PartageDurand)}/medias?q=Main%20gauche");
        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);
        var brut = await liste.Content.ReadAsStringAsync();

        Assert.Contains("La Main gauche de la nuit", brut);
        Assert.DoesNotContain("Bureau", brut);
        Assert.DoesNotContain("Tante Jeanne", brut);
        Assert.DoesNotContain("location", brut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("addedAt", brut, StringComparison.OrdinalIgnoreCase);

        var page = await liste.Content.ReadFromJsonAsync<PagedResponse<SharedMediaResponse>>(CatalogueApiFixture.Json);
        Assert.Equal(SharedAvailability.Lent, Assert.Single(page!.Items).Availability);

        var detail = await Service().GetStringAsync($"{Partage(PartageDurand)}/medias/{livre.Id}");
        Assert.Contains("Ace Books", detail);
        Assert.DoesNotContain("Bureau", detail);
        Assert.DoesNotContain("Tante Jeanne", detail);
    }

    [Fact]
    public async Task Une_demande_arrive_chez_le_preteur_et_se_suit_chez_le_demandeur()
    {
        var livre = await EnregistreChezA("Les Dépossédés");

        var demande = await Demande(PartageDurand, livre.Id);
        Assert.Equal(HttpStatusCode.Created, demande.StatusCode);
        var creee = (await demande.Content.ReadFromJsonAsync<SharedLoanResponse>(CatalogueApiFixture.Json))!;
        Assert.Equal(LoanStatus.Requested, creee.Status);

        // Rejouée, elle rend la même demande.
        var rejouee = await Demande(PartageDurand, livre.Id);
        Assert.Equal(HttpStatusCode.OK, rejouee.StatusCode);
        Assert.Equal(creee.Id, (await rejouee.Content.ReadFromJsonAsync<SharedLoanResponse>(CatalogueApiFixture.Json))!.Id);

        Assert.Equal(SharedAvailability.RequestedByYou, (await Fiche(PartageDurand, livre.Id)).Availability);
        Assert.Equal(SharedAvailability.Available, (await Fiche(PartagePetit, livre.Id)).Availability);

        // Le prêteur la voit, avec le libellé copié.
        var preteur = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var recue = Assert.Single((await preteur.GetFromJsonAsync<List<LoanResponse>>(
            $"/loans?mediaId={livre.Id}", CatalogueApiFixture.Json))!);
        Assert.Equal("Famille Durand (bruno)", recue.BorrowerName);
        Assert.True(recue.FromSharedHousehold);

        Assert.Equal(HttpStatusCode.NoContent, (await preteur.PostAsync($"/loans/{recue.Id}/accept", null)).StatusCode);

        Assert.Equal(SharedAvailability.LentToYou, (await Fiche(PartageDurand, livre.Id)).Availability);
        Assert.Equal(SharedAvailability.Lent, (await Fiche(PartagePetit, livre.Id)).Availability);

        var suivi = await Service().GetFromJsonAsync<List<SharedLoanResponse>>(
            $"{Partage(PartageDurand)}/loan-requests", CatalogueApiFixture.Json);
        Assert.Contains(suivi!, l => l.Id == creee.Id && l.Status == LoanStatus.Active);

        // Un autre partage ne voit pas cette demande.
        var autre = await Service().GetFromJsonAsync<List<SharedLoanResponse>>(
            $"{Partage(PartagePetit)}/loan-requests", CatalogueApiFixture.Json);
        Assert.DoesNotContain(autre!, l => l.Id == creee.Id);

        // Un média prêté ne se demande pas.
        Assert.Equal(HttpStatusCode.Conflict, (await Demande(PartagePetit, livre.Id, "Famille Petit (lea)")).StatusCode);
    }

    [Fact]
    public async Task Seul_le_partage_qui_a_demande_retire_sa_demande()
    {
        var livre = await EnregistreChezA("Le Monde de Rocannon");
        var demande = (await (await Demande(PartageDurand, livre.Id))
            .Content.ReadFromJsonAsync<SharedLoanResponse>(CatalogueApiFixture.Json))!;

        var intrus = await Service().DeleteAsync($"{Partage(PartagePetit)}/loan-requests/{demande.Id}");
        Assert.Equal(HttpStatusCode.NotFound, intrus.StatusCode);

        var retrait = await Service().DeleteAsync($"{Partage(PartageDurand)}/loan-requests/{demande.Id}");
        Assert.Equal(HttpStatusCode.NoContent, retrait.StatusCode);

        Assert.Equal(SharedAvailability.Available, (await Fiche(PartageDurand, livre.Id)).Availability);

        var deNouveau = await Service().DeleteAsync($"{Partage(PartageDurand)}/loan-requests/{demande.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deNouveau.StatusCode);
    }

    [Fact]
    public async Task La_vue_partagee_ne_lit_que_le_menage_designe()
    {
        var livre = await EnregistreChezA("Le Nom du monde est forêt");

        // Le même partage, pointé sur un autre ménage : le média de A n'y existe pas.
        var ailleurs = await Service().GetAsync($"{Partage(PartageDurand, CatalogueApiFixture.HouseholdB)}/medias/{livre.Id}");
        Assert.Equal(HttpStatusCode.NotFound, ailleurs.StatusCode);
    }

    [Fact]
    public async Task Un_membre_de_menage_n_atteint_pas_les_endpoints_de_partage()
    {
        var adulte = fixture.ClientFor(CatalogueApiFixture.HouseholdB, Roles.Adult, Roles.HouseholdAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await adulte.GetAsync($"{Partage(PartageDurand)}/medias")).StatusCode);

        // Le rôle de partage porté avec un ménage : refusé, on ne cumule pas.
        var cumul = fixture.ClientFor(CatalogueApiFixture.HouseholdB, Roles.Sharing);
        Assert.Equal(HttpStatusCode.Forbidden, (await cumul.GetAsync($"{Partage(PartageDurand)}/medias")).StatusCode);
    }

    [Fact]
    public async Task Chaque_role_de_service_n_ouvre_que_ses_endpoints()
    {
        var provisionnement = fixture.ClientWithToken(TestTokens.WithoutHousehold(Roles.Provisioning));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await provisionnement.GetAsync($"{Partage(PartageDurand)}/medias")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Service().PutAsync("/admin/households/par_le_partage/schema", null)).StatusCode);

        // Ni le catalogue d'un membre.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Service().GetAsync("/medias")).StatusCode);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("Majuscules")]
    public async Task Un_identifiant_de_menage_invalide_est_introuvable(string menage)
    {
        var reponse = await Service().GetAsync($"{Partage(PartageDurand, menage)}/medias");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_menage_sans_schema_n_est_pas_servi()
    {
        var reponse = await Service().GetAsync($"{Partage(PartageDurand, CatalogueApiFixture.HouseholdWithoutSchema)}/medias");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reponse.StatusCode);
    }
}
