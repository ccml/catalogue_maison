using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Tenants.Api.Domain;
using Tenants.Api.Features.Households;
using Tenants.Api.Features.Members;
using Tenants.Api.Infrastructure.Persistence;
using Xunit;

namespace Tenants.Api.Tests;

/// <summary>
/// Création d'un ménage par l'opérateur : registre, puis schéma du
/// catalogue, puis premier administrateur. Chaque étape interrompue se
/// reprend sans rien dupliquer.
/// </summary>
public sealed class HouseholdCreationTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    private HttpClient Operator() => fixture.ClientFor(Guid.NewGuid(), null, Roles.PlatformAdmin);

    private static CreateHouseholdAdminRequest Admin(string username = "camille") =>
        new(username, "Camille", "Petit", "temporaire-1");

    [Fact]
    public async Task Un_menage_cree_est_actif_et_son_schema_demande_au_catalogue()
    {
        await fixture.ResetAsync();

        var response = await Operator().PostAsJsonAsync("/households",
            new CreateHouseholdRequest("petit", "Famille Petit"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var household = await response.Content.ReadFromJsonAsync<HouseholdResponse>(TenantsApiFixture.Json);
        Assert.Equal("petit", household!.Id);
        Assert.Equal("Famille Petit", household.DisplayName);
        Assert.Equal(HouseholdStatus.Active, household.Status);
        Assert.Equal(0, household.MemberCount);
        Assert.Equal(["petit"], fixture.Catalogue.Provisioned);
    }

    [Fact]
    public async Task Une_panne_du_catalogue_laisse_le_menage_en_cours_et_la_creation_se_reprend()
    {
        await fixture.ResetAsync();
        fixture.Catalogue.FailNext = true;

        var failed = await Operator().PostAsJsonAsync("/households",
            new CreateHouseholdRequest("petit", "Petit"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(HouseholdStatus.Provisioning, (await db.Households.SingleAsync(h => h.Id == "petit")).Status);
        }

        // Tant que le schéma n'est pas confirmé, aucun membre n'y entre.
        var tooEarly = await Operator().PostAsJsonAsync("/households/petit/admins", Admin(), TenantsApiFixture.Json);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Null(fixture.Directory.ByUsername("camille"));

        var resumed = await Operator().PostAsJsonAsync("/households",
            new CreateHouseholdRequest("petit", "Famille Petit"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, resumed.StatusCode);
        var household = await resumed.Content.ReadFromJsonAsync<HouseholdResponse>(TenantsApiFixture.Json);
        Assert.Equal(HouseholdStatus.Active, household!.Status);
        Assert.Equal("Famille Petit", household.DisplayName);
    }

    [Fact]
    public async Task Un_menage_actif_n_est_pas_recree()
    {
        await fixture.ResetAsync();

        var response = await Operator().PostAsJsonAsync("/households",
            new CreateHouseholdRequest(TenantsApiFixture.HouseholdA, "Autre nom"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(fixture.Catalogue.Provisioned);
        await using var db = fixture.CreateDbContext();
        Assert.Equal("Martin", (await db.Households.SingleAsync(h => h.Id == TenantsApiFixture.HouseholdA)).DisplayName);
    }

    [Theory]
    [InlineData("Majuscules")]
    [InlineData("accentué")]
    [InlineData("tiret-interdit")]
    [InlineData("1chiffre")]
    [InlineData("public")]
    [InlineData("pg_menage")]
    [InlineData("")]
    public async Task Un_identifiant_invalide_ou_reserve_est_refuse(string id)
    {
        await fixture.ResetAsync();

        var response = await Operator().PostAsJsonAsync("/households",
            new CreateHouseholdRequest(id, "Nom"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fixture.Catalogue.Provisioned);
    }

    [Fact]
    public async Task Le_nom_affiche_est_requis()
    {
        await fixture.ResetAsync();

        var response = await Operator().PostAsJsonAsync("/households",
            new CreateHouseholdRequest("petit", "  "), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task L_operateur_ajoute_un_administrateur_adulte_au_menage()
    {
        await fixture.ResetAsync();
        await Operator().PostAsJsonAsync("/households", new CreateHouseholdRequest("petit", "Petit"), TenantsApiFixture.Json);

        var response = await Operator().PostAsJsonAsync("/households/petit/admins", Admin(), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<MemberResponse>(TenantsApiFixture.Json);
        Assert.Equal(MemberRole.Adult, member!.Role);
        Assert.True(member.IsAdmin);
        Assert.False(member.IsSelf);

        var account = fixture.Directory.ByUsername("camille")!;
        Assert.Equal("petit", account.Tenant);
        Assert.True(account.PasswordTemporary);
        Assert.True(account.Roles.SetEquals([Roles.Adult, Roles.HouseholdAdmin]));
        Assert.DoesNotContain(Roles.PlatformAdmin, account.Roles);

        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Members.AnyAsync(m => m.Subject == account.Id && m.HouseholdId == "petit"));
    }

    [Fact]
    public async Task Un_administrateur_pour_un_menage_inconnu_est_refuse()
    {
        await fixture.ResetAsync();

        var response = await Operator().PostAsJsonAsync("/households/inconnu/admins", Admin(), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(fixture.Directory.ByUsername("camille"));
    }

    [Fact]
    public async Task Un_identifiant_de_connexion_deja_pris_ailleurs_est_refuse()
    {
        await fixture.ResetAsync();
        await Operator().PostAsJsonAsync("/households", new CreateHouseholdRequest("petit", "Petit"), TenantsApiFixture.Json);

        var response = await Operator().PostAsJsonAsync("/households/petit/admins", Admin("alice"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TenantsApiFixture.HouseholdA, fixture.Directory.ByUsername("alice")!.Tenant);
    }

    [Fact]
    public async Task Un_administrateur_de_menage_ne_cree_ni_menage_ni_administrateur_ailleurs()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var client = fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin);

        var create = await client.PostAsJsonAsync("/households", new CreateHouseholdRequest("petit", "Petit"), TenantsApiFixture.Json);
        var admin = await client.PostAsJsonAsync($"/households/{TenantsApiFixture.HouseholdB}/admins", Admin(), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        Assert.Empty(fixture.Catalogue.Provisioned);
        Assert.Null(fixture.Directory.ByUsername("camille"));
    }
}
