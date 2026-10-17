using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Features.Provisioning;
using Catalogue.Api.Infrastructure;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Le provisionnement est le seul endpoint où le ménage vient de l'URL. Il
/// n'est ouvert qu'au compte de service de Tenants.Api : rôle de
/// provisionnement, sans claim <c>tenant</c>.
/// </summary>
public sealed class ProvisioningTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private HttpClient ProvisioningService() => fixture.ClientWithToken(TestTokens.WithoutHousehold(Roles.Provisioning));

    [Fact]
    public async Task Le_schema_provisionne_est_servi_et_le_provisionnement_est_rejouable()
    {
        const string household = "nouveau_menage";

        var first = await ProvisioningService().PutAsync($"/admin/households/{household}/schema", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var applied = await first.Content.ReadFromJsonAsync<SchemaProvisionedResponse>(CatalogueApiFixture.Json);
        Assert.True(applied!.AppliedMigrations > 0);

        var replay = await ProvisioningService().PutAsync($"/admin/households/{household}/schema", null);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var again = await replay.Content.ReadFromJsonAsync<SchemaProvisionedResponse>(CatalogueApiFixture.Json);
        Assert.Equal(0, again!.AppliedMigrations);

        // Le garde de version laisse passer le nouveau ménage, vide.
        var medias = await fixture.ClientFor(household, Roles.Adult).GetAsync("/medias");
        Assert.Equal(HttpStatusCode.OK, medias.StatusCode);
    }

    [Theory]
    [InlineData("Majuscules")]
    [InlineData("public")]
    [InlineData("pg_catalog")]
    [InlineData("information_schema")]
    public async Task Un_identifiant_invalide_ou_reserve_est_refuse(string household)
    {
        var response = await ProvisioningService().PutAsync($"/admin/households/{household}/schema", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Un_membre_d_un_menage_ne_provisionne_rien()
    {
        var response = await fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .PutAsync("/admin/households/intrus/schema", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Le_role_de_provisionnement_avec_un_menage_est_refuse()
    {
        var response = await fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Provisioning)
            .PutAsync("/admin/households/intrus/schema", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Un_jeton_sans_menage_ni_role_reste_rejete_en_401()
    {
        var response = await fixture.ClientWithToken(TestTokens.WithoutHousehold(Roles.Adult))
            .PutAsync("/admin/households/intrus/schema", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Le_compte_de_provisionnement_ne_lit_aucun_catalogue()
    {
        var response = await ProvisioningService().GetAsync("/medias");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task La_suppression_efface_le_schema_et_le_menage_n_est_plus_servi()
    {
        const string household = "a_supprimer";
        await ProvisioningService().PutAsync($"/admin/households/{household}/schema", null);
        var member = fixture.ClientFor(household, Roles.Adult);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/medias")).StatusCode);

        var dropped = await ProvisioningService().DeleteAsync($"/admin/households/{household}/schema");
        Assert.Equal(HttpStatusCode.OK, dropped.StatusCode);
        Assert.True((await dropped.Content.ReadFromJsonAsync<SchemaDroppedResponse>(CatalogueApiFixture.Json))!.Existed);

        // Un jeton encore valide ne retombe sur rien : refus franc.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await member.GetAsync("/medias")).StatusCode);

        var again = await ProvisioningService().DeleteAsync($"/admin/households/{household}/schema");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False((await again.Content.ReadFromJsonAsync<SchemaDroppedResponse>(CatalogueApiFixture.Json))!.Existed);
    }

    [Fact]
    public async Task La_suppression_ne_touche_pas_aux_autres_menages()
    {
        await ProvisioningService().PutAsync("/admin/households/voisin_jetable/schema", null);
        await ProvisioningService().DeleteAsync("/admin/households/voisin_jetable/schema");

        var response = await fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult).GetAsync("/medias");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("pg_catalog")]
    [InlineData("information_schema")]
    public async Task Un_schema_reserve_n_est_jamais_supprime(string household)
    {
        var response = await ProvisioningService().DeleteAsync($"/admin/households/{household}/schema");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Un_membre_ne_supprime_aucun_schema()
    {
        var response = await fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .DeleteAsync($"/admin/households/{CatalogueApiFixture.HouseholdB}/schema");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await fixture.ClientFor(CatalogueApiFixture.HouseholdB, Roles.Adult).GetAsync("/medias")).StatusCode);
    }
}
