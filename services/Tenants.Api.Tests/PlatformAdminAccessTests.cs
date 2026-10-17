using System.Net;
using System.Net.Http.Json;
using Tenants.Api.Domain;
using Tenants.Api.Features.Households;
using Xunit;

namespace Tenants.Api.Tests;

/// <summary>
/// L'opérateur est la seule exception à « pas de ménage, pas d'accès », et
/// elle est étroite : un rôle précis, aucun claim <c>tenant</c>, et les seuls
/// endpoints de plateforme. Ne pas élargir sans revoir chacun de ces tests.
/// </summary>
public sealed class PlatformAdminAccessTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    [Fact]
    public async Task L_operateur_sans_menage_liste_les_menages()
    {
        await fixture.ResetAsync();

        var households = await fixture.ClientFor(Guid.NewGuid(), null, Roles.PlatformAdmin)
            .GetFromJsonAsync<List<HouseholdResponse>>("/households", TenantsApiFixture.Json);

        Assert.NotNull(households);
        Assert.Equal([TenantsApiFixture.HouseholdB, TenantsApiFixture.HouseholdA], households.Select(h => h.Id));
        Assert.All(households, h => Assert.Equal(1, h.MemberCount));
    }

    [Fact]
    public async Task L_operateur_n_accede_pas_aux_membres()
    {
        await fixture.ResetAsync();

        var response = await fixture.ClientFor(Guid.NewGuid(), null, Roles.PlatformAdmin).GetAsync("/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Un_administrateur_de_menage_n_administre_pas_les_menages()
    {
        var (adminA, _) = await fixture.ResetAsync();

        var response = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.HouseholdAdmin)
            .GetAsync("/households");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Un_membre_ne_cumule_pas_les_deux_casquettes()
    {
        var (adminA, _) = await fixture.ResetAsync();

        var response = await fixture.ClientFor(adminA, TenantsApiFixture.HouseholdA, Roles.Adult, Roles.PlatformAdmin)
            .GetAsync("/households");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Un_compte_sans_menage_ni_role_de_plateforme_reste_rejete_en_401()
    {
        await fixture.ResetAsync();

        var response = await fixture.ClientFor(Guid.NewGuid(), null, Roles.Adult).GetAsync("/households");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Un_claim_tenant_invalide_n_est_pas_une_absence()
    {
        await fixture.ResetAsync();

        var response = await fixture.ClientFor(Guid.NewGuid(), "Pas-Un-Menage", Roles.PlatformAdmin).GetAsync("/households");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Le_role_de_plateforme_ne_peut_pas_etre_accorde_par_un_administrateur_de_menage() =>
        Assert.DoesNotContain(Roles.PlatformAdmin, Roles.Managed);
}
