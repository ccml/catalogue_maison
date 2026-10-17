using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Tenants.Api.Domain;
using Tenants.Api.Features.Members;
using Tenants.Api.Infrastructure.Persistence;
using Xunit;

namespace Tenants.Api.Tests;

/// <summary>
/// Détection des écarts entre Keycloak et le registre, et leur réparation —
/// toujours explicite, jamais automatique, et bornée au ménage de
/// l'administrateur.
/// </summary>
public sealed class InconsistencyTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    private async Task AddEntryAsync(Guid subject, string household, string username)
    {
        await using var db = fixture.CreateDbContext();
        db.Members.Add(new Member
        {
            Subject = subject,
            HouseholdId = household,
            Username = username,
            RegisteredAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<bool> HasEntryAsync(Guid subject)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Members.AnyAsync(m => m.Subject == subject);
    }

    [Fact]
    public async Task Les_deux_sortes_d_ecart_sont_detectees_sans_rien_corriger()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var theo = fixture.Directory.Seed("theo", TenantsApiFixture.HouseholdA, Roles.Child);
        var disparu = Guid.NewGuid();
        await AddEntryAsync(disparu, TenantsApiFixture.HouseholdA, "disparu");
        var parti = fixture.Directory.Seed("parti", TenantsApiFixture.HouseholdB, Roles.Adult);
        await AddEntryAsync(parti, TenantsApiFixture.HouseholdA, "parti");

        var report = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .GetFromJsonAsync<InconsistenciesResponse>("/members/inconsistencies", TenantsApiFixture.Json);

        Assert.NotNull(report);
        Assert.Equal(theo, Assert.Single(report.UnregisteredAccounts).Id);
        Assert.Equal(
            [("disparu", OrphanReason.AccountMissing), ("parti", OrphanReason.AccountDetached)],
            report.OrphanEntries.Select(o => (o.Username, o.Reason)));

        // Rien n'a bougé.
        Assert.False(await HasEntryAsync(theo));
        Assert.True(await HasEntryAsync(disparu));
    }

    [Fact]
    public async Task Les_ecarts_d_un_autre_menage_ne_sont_pas_visibles()
    {
        var (adminA, _) = await fixture.ResetAsync();
        fixture.Directory.Seed("intrus", TenantsApiFixture.HouseholdB, Roles.Adult);
        await AddEntryAsync(Guid.NewGuid(), TenantsApiFixture.HouseholdB, "fantome");

        var report = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .GetFromJsonAsync<InconsistenciesResponse>("/members/inconsistencies", TenantsApiFixture.Json);

        Assert.NotNull(report);
        Assert.Empty(report.UnregisteredAccounts);
        Assert.Empty(report.OrphanEntries);
    }

    [Fact]
    public async Task Inscrire_un_compte_hors_registre_est_idempotent()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var theo = fixture.Directory.Seed("theo", TenantsApiFixture.HouseholdA, Roles.Child);
        var client = fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin);

        var first = await client.PostAsync($"/members/{theo}/register", null);
        var second = await client.PostAsync($"/members/{theo}/register", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True((await second.Content.ReadFromJsonAsync<MemberResponse>(TenantsApiFixture.Json))!.Registered);

        await using var db = fixture.CreateDbContext();
        var entry = await db.Members.SingleAsync(m => m.Subject == theo);
        Assert.Equal(TenantsApiFixture.HouseholdA, entry.HouseholdId);

        // Keycloak n'a pas été touché.
        Assert.Equal([Roles.Child], fixture.Directory[theo].Roles);
        Assert.Null(fixture.Directory[theo].Password);
    }

    [Fact]
    public async Task On_n_inscrit_pas_le_compte_d_un_autre_menage()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var intrus = fixture.Directory.Seed("intrus", TenantsApiFixture.HouseholdB, Roles.Adult);

        var response = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .PostAsync($"/members/{intrus}/register", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await HasEntryAsync(intrus));
    }

    [Fact]
    public async Task Un_compte_encore_inscrit_ailleurs_n_est_pas_deplace_en_silence()
    {
        var (adminA, _) = await fixture.ResetAsync();
        // Passé de durand à martin dans Keycloak, encore inscrit chez durand.
        var demenage = fixture.Directory.Seed("demenage", TenantsApiFixture.HouseholdA, Roles.Adult);
        await AddEntryAsync(demenage, TenantsApiFixture.HouseholdB, "demenage");

        var response = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .PostAsync($"/members/{demenage}/register", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("inscrit-ailleurs", await response.Content.ReadAsStringAsync());

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TenantsApiFixture.HouseholdB, (await db.Members.SingleAsync(m => m.Subject == demenage)).HouseholdId);
    }

    [Fact]
    public async Task Retirer_une_entree_orpheline_est_idempotent()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var disparu = Guid.NewGuid();
        await AddEntryAsync(disparu, TenantsApiFixture.HouseholdA, "disparu");
        var client = fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin);

        var first = await client.DeleteAsync($"/members/{disparu}/registry-entry");
        var second = await client.DeleteAsync($"/members/{disparu}/registry-entry");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.False(await HasEntryAsync(disparu));
    }

    [Fact]
    public async Task Une_entree_coherente_ne_se_retire_pas()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var theo = fixture.Directory.Seed("theo", TenantsApiFixture.HouseholdA, Roles.Child);
        await AddEntryAsync(theo, TenantsApiFixture.HouseholdA, "theo");

        var response = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .DeleteAsync($"/members/{theo}/registry-entry");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await HasEntryAsync(theo));
    }

    [Fact]
    public async Task L_entree_d_un_autre_menage_n_est_jamais_retiree()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var fantome = Guid.NewGuid();
        await AddEntryAsync(fantome, TenantsApiFixture.HouseholdB, "fantome");
        var client = fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin);

        // Même réponse que pour une entrée absente : rien n'est révélé.
        var orphan = await client.DeleteAsync($"/members/{fantome}/registry-entry");
        var coherent = await client.DeleteAsync($"/members/{adminB}/registry-entry");

        Assert.Equal(HttpStatusCode.NoContent, orphan.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, coherent.StatusCode);
        Assert.True(await HasEntryAsync(fantome));
        Assert.True(await HasEntryAsync(adminB));
    }

    [Fact]
    public async Task Un_adulte_non_administrateur_ne_voit_pas_les_ecarts()
    {
        await fixture.ResetAsync();
        var paul = fixture.Directory.Seed("paul", TenantsApiFixture.HouseholdA, Roles.Adult);

        var response = await fixture.ClientFor(paul, TenantsApiFixture.HouseholdA, Roles.Adult)
            .GetAsync("/members/inconsistencies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
