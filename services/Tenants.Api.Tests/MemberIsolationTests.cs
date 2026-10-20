using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Tenants.Api.Domain;
using Tenants.Api.Features.Members;
using Xunit;

namespace Tenants.Api.Tests;

/// <summary>
/// Le garde-fou de l'administration : un administrateur n'agit que sur son
/// ménage, désigné par son jeton. Ne jamais neutraliser un de ces tests.
/// </summary>
public sealed class MemberIsolationTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    [Fact]
    public async Task La_liste_ne_contient_que_les_comptes_du_menage_de_l_administrateur()
    {
        var (adminA, _) = await fixture.ResetAsync();
        fixture.Directory.Seed("theo", TenantsApiFixture.HouseholdA, Roles.Child);
        fixture.Directory.Seed("orphelin", null, Roles.Adult);

        var members = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .GetFromJsonAsync<List<MemberResponse>>("/members", TenantsApiFixture.Json);

        Assert.NotNull(members);
        Assert.Equal(["alice", "theo"], members.Select(m => m.Username).Order());
    }

    [Fact]
    public async Task Un_compte_d_un_autre_menage_est_introuvable_par_tous_les_chemins()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var client = fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin);

        var role = await client.PutAsJsonAsync($"/members/{adminB}/role",
            new UpdateMemberRoleRequest(MemberRole.Child, false), TenantsApiFixture.Json);
        var password = await client.PutAsJsonAsync($"/members/{adminB}/password",
            new ResetPasswordRequest("piratage-123"), TenantsApiFixture.Json);
        var deactivate = await client.PostAsync($"/members/{adminB}/deactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, role.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, password.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deactivate.StatusCode);

        var bruno = fixture.Directory[adminB];
        Assert.True(bruno.Enabled);
        Assert.Null(bruno.Password);
        Assert.Contains(Roles.HouseholdAdmin, bruno.Roles);
    }

    [Fact]
    public async Task Un_compte_desactive_d_un_autre_menage_ne_peut_pas_etre_supprime()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var client = fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin);
        fixture.Directory[adminB].Enabled = false;

        var response = await client.DeleteAsync($"/members/{adminB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(fixture.Directory.ByUsername("bruno"));
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Members.AnyAsync(m => m.Subject == adminB));
    }

    [Fact]
    public async Task La_reprise_d_une_suppression_ne_touche_pas_au_registre_d_un_autre_menage()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var client = fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin);
        // Compte de B disparu de Keycloak, entrée encore au registre de B.
        await fixture.Directory.DeleteAsync(adminB, CancellationToken.None);

        var response = await client.DeleteAsync($"/members/{adminB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Members.AnyAsync(m => m.Subject == adminB));
    }

    [Fact]
    public async Task Un_identifiant_pris_dans_un_autre_menage_n_est_ni_repris_ni_modifie()
    {
        var (adminA, adminB) = await fixture.ResetAsync();

        var response = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .PostAsJsonAsync("/members", new CreateMemberRequest(
                "bruno", "Bruno", "Martin", MemberRole.Adult, false, "temporaire-1"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TenantsApiFixture.HouseholdB, fixture.Directory[adminB].Tenant);
        Assert.Null(fixture.Directory[adminB].Password);
    }

    [Fact]
    public async Task Le_menage_du_nouveau_membre_est_celui_du_jeton()
    {
        var (adminA, _) = await fixture.ResetAsync();

        // Un champ « tenant » glissé dans le corps n'a aucun effet : le
        // contrat ne le connaît pas.
        var response = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .PostAsJsonAsync("/members", new
            {
                username = "lea",
                firstName = "Léa",
                lastName = "Martin",
                role = "Child",
                isAdmin = false,
                temporaryPassword = "temporaire-1",
                tenant = TenantsApiFixture.HouseholdB
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(TenantsApiFixture.HouseholdA, fixture.Directory.ByUsername("lea")!.Tenant);
    }

    [Fact]
    public async Task Un_adulte_non_administrateur_n_a_pas_acces()
    {
        await fixture.ResetAsync();
        var theo = fixture.Directory.Seed("paul", TenantsApiFixture.HouseholdA, Roles.Adult);

        var response = await fixture.ClientFor(theo, TenantsApiFixture.HouseholdA, Roles.Adult).GetAsync("/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Un_jeton_sans_menage_est_rejete_en_401()
    {
        await fixture.ResetAsync();

        var response = await fixture.ClientFor(Guid.NewGuid(), null, Roles.Adult, Roles.HouseholdAdmin).GetAsync("/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Un_jeton_destine_a_un_autre_service_est_rejete()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TenantsApiFixture.Token(
            adminA, TenantsApiFixture.HouseholdA, [Roles.Adult, Roles.HouseholdAdmin], audience: "catalogue-api"));

        var response = await client.GetAsync("/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
