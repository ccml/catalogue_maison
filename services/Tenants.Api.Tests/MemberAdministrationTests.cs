using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Tenants.Api.Domain;
using Tenants.Api.Features.Members;
using Xunit;

namespace Tenants.Api.Tests;

public sealed class MemberAdministrationTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    private async Task<(Guid Admin, HttpClient Client)> AdminOfAAsync()
    {
        var (adminA, _) = await fixture.ResetAsync();
        return (adminA, fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin));
    }

    [Fact]
    public async Task Ajouter_un_membre_cree_le_compte_puis_l_inscrit_au_registre()
    {
        var (_, client) = await AdminOfAAsync();

        var response = await client.PostAsJsonAsync("/members", new CreateMemberRequest(
            "  Theo ", "Théo", "Martin", MemberRole.Child, false, "temporaire-1"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<MemberResponse>(TenantsApiFixture.Json);
        Assert.NotNull(member);
        Assert.Equal("theo", member.Username);
        Assert.Equal(MemberRole.Child, member.Role);
        Assert.False(member.IsAdmin);
        Assert.True(member.Registered);

        var account = fixture.Directory[member.Id];
        Assert.Equal([Roles.Child], account.Roles);
        Assert.True(account.PasswordTemporary);

        await using var db = fixture.CreateDbContext();
        var entry = await db.Members.SingleAsync(m => m.Subject == member.Id);
        Assert.Equal(TenantsApiFixture.HouseholdA, entry.HouseholdId);
    }

    [Fact]
    public async Task Une_creation_interrompue_se_rejoue_sans_doublon()
    {
        var (_, client) = await AdminOfAAsync();
        var request = new CreateMemberRequest("lea", "Léa", "Martin", MemberRole.Adult, true, "temporaire-1");

        // Le compte est créé dans Keycloak, puis l'attribution des rôles échoue.
        fixture.Directory.FailNextRoleUpdate = true;
        var first = await client.PostAsJsonAsync("/members", request, TenantsApiFixture.Json);
        Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);

        var replay = await client.PostAsJsonAsync("/members", request, TenantsApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);

        var lea = Assert.Single(fixture.Directory.Accounts, a => a.Username == "lea");
        Assert.Equal([Roles.HouseholdAdmin, Roles.Adult], lea.Roles.Order(StringComparer.Ordinal));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Members.CountAsync(m => m.Subject == lea.Id));

        // Une fois inscrit, le même identifiant est pris.
        var third = await client.PostAsJsonAsync("/members", request, TenantsApiFixture.Json);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
    }

    [Fact]
    public async Task Un_compte_present_dans_keycloak_mais_pas_au_registre_est_signale()
    {
        var (_, client) = await AdminOfAAsync();
        fixture.Directory.Seed("theo", TenantsApiFixture.HouseholdA, Roles.Child);

        var members = await client.GetFromJsonAsync<List<MemberResponse>>("/members", TenantsApiFixture.Json);

        Assert.NotNull(members);
        Assert.False(members.Single(m => m.Username == "theo").Registered);
        Assert.True(members.Single(m => m.Username == "alice").Registered);
        Assert.True(members.Single(m => m.Username == "alice").IsSelf);
    }

    [Theory]
    [InlineData(null, "Théo", "Martin", "Child", false, "temporaire-1", "username")]
    [InlineData("t", "Théo", "Martin", "Child", false, "temporaire-1", "username")]
    [InlineData("theo martin", "Théo", "Martin", "Child", false, "temporaire-1", "username")]
    [InlineData("theo", "", "Martin", "Child", false, "temporaire-1", "firstName")]
    [InlineData("theo", "Théo", "Martin", null, false, "temporaire-1", "role")]
    [InlineData("theo", "Théo", "Martin", "Child", true, "temporaire-1", "isAdmin")]
    [InlineData("theo", "Théo", "Martin", "Child", false, "court", "temporaryPassword")]
    public async Task Une_demande_invalide_est_refusee_sans_rien_ecrire(
        string? username, string firstName, string lastName, string? role, bool isAdmin, string password, string field)
    {
        var (_, client) = await AdminOfAAsync();

        var response = await client.PostAsJsonAsync("/members", new
        {
            username,
            firstName,
            lastName,
            role,
            isAdmin,
            temporaryPassword = password
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync());
        Assert.Equal(2, fixture.Directory.Accounts.Count);
    }

    [Fact]
    public async Task Un_role_reserve_aux_services_ne_peut_pas_etre_demande()
    {
        var (_, client) = await AdminOfAAsync();

        var response = await client.PostAsJsonAsync("/members", new
        {
            username = "intrus",
            firstName = "In",
            lastName = "Trus",
            role = "enrichissement",
            isAdmin = false,
            temporaryPassword = "temporaire-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(fixture.Directory.ByUsername("intrus"));
    }

    [Fact]
    public async Task Changer_le_role_ne_touche_qu_aux_roles_de_menage()
    {
        var (_, client) = await AdminOfAAsync();
        var theo = fixture.Directory.Seed("theo", TenantsApiFixture.HouseholdA, Roles.Child, "offline_access");

        var response = await client.PutAsJsonAsync($"/members/{theo}/role",
            new UpdateMemberRoleRequest(MemberRole.Adult, true), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([Roles.HouseholdAdmin, Roles.Adult, "offline_access"], fixture.Directory[theo].Roles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task L_administrateur_ne_peut_ni_se_desactiver_ni_se_retirer_l_administration()
    {
        var (admin, client) = await AdminOfAAsync();

        var demotion = await client.PutAsJsonAsync($"/members/{admin}/role",
            new UpdateMemberRoleRequest(MemberRole.Adult, false), TenantsApiFixture.Json);
        var deactivation = await client.PostAsync($"/members/{admin}/deactivate", null);

        Assert.Equal(HttpStatusCode.Conflict, demotion.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deactivation.StatusCode);
        Assert.True(fixture.Directory[admin].Enabled);
        Assert.Contains(Roles.HouseholdAdmin, fixture.Directory[admin].Roles);
    }

    [Fact]
    public async Task Desactiver_ferme_les_sessions_et_se_note_au_registre()
    {
        var (_, client) = await AdminOfAAsync();
        var created = await (await client.PostAsJsonAsync("/members", new CreateMemberRequest(
                "theo", "Théo", "Martin", MemberRole.Child, false, "temporaire-1"), TenantsApiFixture.Json))
            .Content.ReadFromJsonAsync<MemberResponse>(TenantsApiFixture.Json);

        var deactivated = await client.PostAsync($"/members/{created!.Id}/deactivate", null);

        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.False(fixture.Directory[created.Id].Enabled);
        Assert.Equal(1, fixture.Directory[created.Id].SignOuts);

        await using (var db = fixture.CreateDbContext())
        {
            Assert.NotNull((await db.Members.SingleAsync(m => m.Subject == created.Id)).DeactivatedAt);
        }

        var reactivated = await client.PostAsync($"/members/{created.Id}/reactivate", null);

        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.True(fixture.Directory[created.Id].Enabled);

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Null((await db.Members.SingleAsync(m => m.Subject == created.Id)).DeactivatedAt);
        }
    }

    [Fact]
    public async Task Reinitialiser_le_mot_de_passe_le_rend_temporaire()
    {
        var (_, client) = await AdminOfAAsync();
        var theo = fixture.Directory.Seed("theo", TenantsApiFixture.HouseholdA, Roles.Child);

        var response = await client.PutAsJsonAsync($"/members/{theo}/password",
            new ResetPasswordRequest("nouveau-123"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("nouveau-123", fixture.Directory[theo].Password);
        Assert.True(fixture.Directory[theo].PasswordTemporary);
    }

    [Fact]
    public async Task Un_menage_absent_du_registre_ne_recoit_aucun_compte()
    {
        await fixture.ResetAsync();
        var admin = fixture.Directory.Seed("zoe", "dupont", Roles.Adult, Roles.HouseholdAdmin);

        var response = await fixture.ClientFor(admin, "dupont", Roles.Adult, Roles.HouseholdAdmin)
            .PostAsJsonAsync("/members", new CreateMemberRequest(
                "max", "Max", "Dupont", MemberRole.Child, false, "temporaire-1"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(fixture.Directory.ByUsername("max"));
    }
}
