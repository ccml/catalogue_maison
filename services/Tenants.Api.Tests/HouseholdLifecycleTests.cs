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
/// Fin de vie d'un ménage, en deux temps : archiver (réversible), puis
/// purger (irréversible, et seulement après archivage).
/// </summary>
public sealed class HouseholdLifecycleTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    private HttpClient Operator() => fixture.ClientFor(Guid.NewGuid(), null, Roles.PlatformAdmin);

    private async Task<Household> HouseholdAsync(string id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Households.AsNoTracking().SingleAsync(h => h.Id == id);
    }

    private static string A => TenantsApiFixture.HouseholdA;

    [Fact]
    public async Task L_archivage_desactive_les_comptes_et_ferme_leurs_sessions()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var theo = fixture.Directory.Seed("theo", A, Roles.Child);

        var response = await Operator().PostAsync($"/households/{A}/archive", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var household = await response.Content.ReadFromJsonAsync<HouseholdResponse>(TenantsApiFixture.Json);
        Assert.Equal(HouseholdStatus.Archived, household!.Status);
        Assert.NotNull(household.ArchivedAt);

        foreach (var id in new[] { adminA, theo })
        {
            Assert.False(fixture.Directory[id].Enabled);
            Assert.True(fixture.Directory[id].SignOuts > 0);
        }

        // Le catalogue n'est pas touché, l'autre ménage non plus.
        Assert.Empty(fixture.Catalogue.Dropped);
        Assert.True(fixture.Directory[adminB].Enabled);
    }

    [Fact]
    public async Task Un_administrateur_ne_reactive_rien_dans_un_menage_archive()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var theo = fixture.Directory.Seed("theo", A, Roles.Child);
        await Operator().PostAsync($"/households/{A}/archive", null);

        // Jeton d'administrateur émis avant l'archivage, encore valide.
        var admin = fixture.ClientFor(adminA, A, Roles.Adult, Roles.HouseholdAdmin);
        var reactivate = await admin.PostAsync($"/members/{theo}/reactivate", null);
        var create = await admin.PostAsJsonAsync("/members", new CreateMemberRequest(
            "lea", "Léa", "Martin", MemberRole.Adult, false, "temporaire-1"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, reactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.False(fixture.Directory[theo].Enabled);
        Assert.Null(fixture.Directory.ByUsername("lea"));
    }

    [Fact]
    public async Task La_restauration_respecte_les_desactivations_de_l_administrateur()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var theo = fixture.Directory.Seed("theo", A, Roles.Child);
        var ancien = fixture.Directory.Seed("ancien", A, Roles.Adult);
        await using (var db = fixture.CreateDbContext())
        {
            db.Members.AddRange(
                new Member { Subject = theo, HouseholdId = A, Username = "theo", RegisteredAt = DateTimeOffset.UtcNow },
                new Member
                {
                    Subject = ancien, HouseholdId = A, Username = "ancien", RegisteredAt = DateTimeOffset.UtcNow,
                    DeactivatedAt = DateTimeOffset.UtcNow
                });
            await db.SaveChangesAsync();
        }
        fixture.Directory[ancien].Enabled = false;

        await Operator().PostAsync($"/households/{A}/archive", null);
        var response = await Operator().PostAsync($"/households/{A}/restore", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var household = await response.Content.ReadFromJsonAsync<HouseholdResponse>(TenantsApiFixture.Json);
        Assert.Equal(HouseholdStatus.Active, household!.Status);
        Assert.Null(household.ArchivedAt);
        Assert.True(fixture.Directory[adminA].Enabled);
        Assert.True(fixture.Directory[theo].Enabled);
        Assert.False(fixture.Directory[ancien].Enabled);
    }

    [Fact]
    public async Task Un_menage_actif_ne_se_purge_pas()
    {
        var (adminA, _) = await fixture.ResetAsync();

        var response = await Operator().PostAsJsonAsync($"/households/{A}/purge",
            new PurgeHouseholdRequest(A), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(fixture.Catalogue.Dropped);
        Assert.True(fixture.Directory[adminA].Enabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("durand")]
    [InlineData("Martin")]
    public async Task La_purge_exige_de_ressaisir_l_identifiant(string? confirmation)
    {
        await fixture.ResetAsync();
        await Operator().PostAsync($"/households/{A}/archive", null);

        var response = await Operator().PostAsJsonAsync($"/households/{A}/purge",
            new PurgeHouseholdRequest(confirmation), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fixture.Catalogue.Dropped);
        Assert.Equal(HouseholdStatus.Archived, (await HouseholdAsync(A)).Status);
    }

    [Fact]
    public async Task La_purge_supprime_catalogue_comptes_et_registre_et_seulement_eux()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var theo = fixture.Directory.Seed("theo", A, Roles.Child);
        await Operator().PostAsync($"/households/{A}/archive", null);

        var response = await Operator().PostAsJsonAsync($"/households/{A}/purge",
            new PurgeHouseholdRequest(A), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([A], fixture.Catalogue.Dropped);
        Assert.DoesNotContain(fixture.Directory.Accounts, a => a.Id == adminA || a.Id == theo);
        Assert.True(fixture.Directory[adminB].Enabled);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Households.AnyAsync(h => h.Id == A));
        Assert.False(await db.Members.AnyAsync(m => m.HouseholdId == A));
        Assert.True(await db.Members.AnyAsync(m => m.HouseholdId == TenantsApiFixture.HouseholdB));
    }

    [Fact]
    public async Task Une_purge_interrompue_ne_se_restaure_pas_et_se_reprend()
    {
        var (adminA, _) = await fixture.ResetAsync();
        await Operator().PostAsync($"/households/{A}/archive", null);
        fixture.Directory.FailNextDelete = true;

        var failed = await Operator().PostAsJsonAsync($"/households/{A}/purge",
            new PurgeHouseholdRequest(A), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(HouseholdStatus.Purging, (await HouseholdAsync(A)).Status);

        var restore = await Operator().PostAsync($"/households/{A}/restore", null);
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);

        var resumed = await Operator().PostAsJsonAsync($"/households/{A}/purge",
            new PurgeHouseholdRequest(A), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.NoContent, resumed.StatusCode);
        Assert.DoesNotContain(fixture.Directory.Accounts, a => a.Id == adminA);
    }

    [Fact]
    public async Task Une_creation_abandonnee_se_supprime_sans_archivage()
    {
        await fixture.ResetAsync();
        fixture.Catalogue.FailNext = true;
        await Operator().PostAsJsonAsync("/households", new CreateHouseholdRequest("petit", "Petit"), TenantsApiFixture.Json);

        var archive = await Operator().PostAsync("/households/petit/archive", null);
        var purge = await Operator().PostAsJsonAsync("/households/petit/purge",
            new PurgeHouseholdRequest("petit"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, archive.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, purge.StatusCode);
    }

    [Fact]
    public async Task Un_identifiant_purge_redevient_libre()
    {
        await fixture.ResetAsync();
        await Operator().PostAsync($"/households/{A}/archive", null);
        await Operator().PostAsJsonAsync($"/households/{A}/purge", new PurgeHouseholdRequest(A), TenantsApiFixture.Json);

        var recreated = await Operator().PostAsJsonAsync("/households",
            new CreateHouseholdRequest(A, "Nouveaux Martin"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, recreated.StatusCode);
    }

    [Fact]
    public async Task Un_menage_archive_n_est_pas_recree_par_dessus()
    {
        await fixture.ResetAsync();
        await Operator().PostAsync($"/households/{A}/archive", null);

        var response = await Operator().PostAsJsonAsync("/households",
            new CreateHouseholdRequest(A, "Autres Martin"), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HouseholdStatus.Archived, (await HouseholdAsync(A)).Status);
    }

    [Fact]
    public async Task Un_administrateur_de_menage_n_archive_ni_ne_purge_rien()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var admin = fixture.ClientFor(adminA, A, Roles.Adult, Roles.HouseholdAdmin);

        var archive = await admin.PostAsync($"/households/{TenantsApiFixture.HouseholdB}/archive", null);
        var purge = await admin.PostAsJsonAsync($"/households/{A}/purge", new PurgeHouseholdRequest(A), TenantsApiFixture.Json);

        Assert.Equal(HttpStatusCode.Forbidden, archive.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, purge.StatusCode);
        Assert.Equal(HouseholdStatus.Active, (await HouseholdAsync(TenantsApiFixture.HouseholdB)).Status);
    }
}
