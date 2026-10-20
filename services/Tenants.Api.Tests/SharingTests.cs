using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tenants.Api.Domain;
using Tenants.Api.Features.Households;
using Tenants.Api.Features.Sharing;
using Tenants.Api.Infrastructure.Persistence;
using Xunit;

namespace Tenants.Api.Tests;

/// <summary>
/// Le partage de catalogue entre ménages. Le garde-fou : un ménage ne
/// consulte que les catalogues qui lui sont partagés, ne désigne jamais un
/// ménage autrement que par un partage qui lui est accordé, et ne voit
/// jamais l'identifiant d'un autre ménage. Ne jamais neutraliser ces tests.
/// </summary>
public sealed class SharingTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    private const string A = TenantsApiFixture.HouseholdA;
    private const string B = TenantsApiFixture.HouseholdB;
    private const string C = "petit";

    private HttpClient Admin(Guid subject, string household) =>
        fixture.ClientFor(subject, household, Roles.Adult, Roles.HouseholdAdmin);

    private HttpClient Child(string household) => fixture.ClientFor(Guid.NewGuid(), household, Roles.Child);

    private HttpClient Adult(string household) => fixture.ClientFor(Guid.NewGuid(), household, Roles.Adult);

    private HttpClient Operator() => fixture.ClientFor(Guid.NewGuid(), null, Roles.PlatformAdmin);

    private static async Task<string> InviteAsync(HttpClient admin)
    {
        var response = await admin.PostAsync("/sharing/invitations", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedInvitationResponse>(TenantsApiFixture.Json))!.Code;
    }

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient admin, string? code) =>
        admin.PostAsJsonAsync("/sharing/invitations/accept", new AcceptInvitationRequest(code), TenantsApiFixture.Json);

    /// <summary>A partage son catalogue avec B ; rend l'identifiant du partage.</summary>
    private async Task<Guid> ShareAToBAsync(Guid adminA, Guid adminB)
    {
        var accepted = await AcceptAsync(Admin(adminB, B), await InviteAsync(Admin(adminA, A)));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return (await accepted.Content.ReadFromJsonAsync<ShareResponse>(TenantsApiFixture.Json))!.Id;
    }

    private async Task AddHouseholdAsync(string id, string name, HouseholdStatus status = HouseholdStatus.Active)
    {
        await using var db = fixture.CreateDbContext();
        db.Households.Add(new Household { Id = id, DisplayName = name, CreatedAt = DateTimeOffset.UtcNow, Status = status });
        await db.SaveChangesAsync();
    }

    private async Task SetStatusAsync(string id, HouseholdStatus status)
    {
        await using var db = fixture.CreateDbContext();
        await db.Households.Where(h => h.Id == id).ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, status));
    }

    private static async Task<List<ShareResponse>> Received(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ShareResponse>>("/sharing/received", TenantsApiFixture.Json))!;

    [Fact]
    public async Task Une_invitation_acceptee_ouvre_le_catalogue_dans_un_seul_sens()
    {
        var (adminA, adminB) = await fixture.ResetAsync();

        var code = await InviteAsync(Admin(adminA, A));
        Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", code);

        // Saisi en minuscules, sans tiret, avec des espaces : il passe.
        var accepted = await AcceptAsync(Admin(adminB, B), $" {code.Replace("-", " ").ToLowerInvariant()} ");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var share = (await accepted.Content.ReadFromJsonAsync<ShareResponse>(TenantsApiFixture.Json))!;
        Assert.Equal("Martin", share.HouseholdName);

        // B voit le catalogue de A, sous son nom affiché, jamais son identifiant.
        var raw = await Child(B).GetStringAsync("/sharing/received");
        Assert.Contains("Martin", raw);
        Assert.DoesNotContain(A, raw, StringComparison.Ordinal);
        Assert.Equal(share.Id, Assert.Single(await Received(Child(B))).Id);

        // A voit qui consulte son catalogue.
        var granted = await Admin(adminA, A).GetFromJsonAsync<List<ShareResponse>>("/sharing/granted", TenantsApiFixture.Json);
        Assert.Equal("Durand", Assert.Single(granted!).HouseholdName);

        // Le partage est à sens unique.
        Assert.Empty(await Received(Admin(adminA, A)));
        Assert.Equal(HttpStatusCode.NotFound,
            (await Admin(adminA, A).GetAsync($"/sharing/received/{share.Id}/medias")).StatusCode);

        // L'invitation est consommée.
        var invitations = await Admin(adminA, A).GetFromJsonAsync<List<InvitationResponse>>(
            "/sharing/invitations", TenantsApiFixture.Json);
        Assert.Empty(invitations!);
    }

    [Fact]
    public async Task La_consultation_est_relayee_au_catalogue_du_menage_preteur_et_a_lui_seul()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var shareId = await ShareAToBAsync(adminA, adminB);
        fixture.Sharing.Answer = FakeCatalogueSharing.Ok("""{"items":[],"total":0,"page":1,"size":50}""");

        var response = await Child(B).GetAsync($"/sharing/received/{shareId}/medias?q=Horla&type=Book&page=2&menage=durand");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"total\":0", await response.Content.ReadAsStringAsync());

        var call = Assert.Single(fixture.Sharing.Calls);
        Assert.Equal("GET", call.Method);
        Assert.Equal(A, call.Owner);
        Assert.Equal(shareId, call.ShareId);
        Assert.Equal("medias", call.Path);
        Assert.Equal("Horla", call.Query["q"]);
        Assert.Equal("Book", call.Query["type"]);
        Assert.Equal("2", call.Query["page"]);
        // Un paramètre inconnu n'est pas relayé.
        Assert.False(call.Query.ContainsKey("menage"));
    }

    [Fact]
    public async Task Un_menage_sans_partage_n_atteint_rien_par_un_identifiant_de_partage()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        await AddHouseholdAsync(C, "Petit");
        var shareId = await ShareAToBAsync(adminA, adminB);

        var adultC = Adult(C);

        Assert.Empty(await Received(adultC));
        Assert.Equal(HttpStatusCode.NotFound, (await adultC.GetAsync($"/sharing/received/{shareId}/medias")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await adultC.GetAsync($"/sharing/received/{shareId}/medias/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await adultC.PostAsync($"/sharing/received/{shareId}/medias/{Guid.NewGuid()}/loan-requests", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adultC.GetAsync($"/sharing/received/{shareId}/loan-requests")).StatusCode);

        // Un identifiant inventé ne vaut pas mieux.
        Assert.Equal(HttpStatusCode.NotFound,
            (await Child(B).GetAsync($"/sharing/received/{Guid.NewGuid()}/medias")).StatusCode);

        Assert.Empty(fixture.Sharing.Calls);
    }

    [Fact]
    public async Task Un_code_ne_sert_qu_une_fois_et_un_code_errone_ou_expire_est_refuse()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        await AddHouseholdAsync(C, "Petit");
        var adminC = Guid.NewGuid();

        var code = await InviteAsync(Admin(adminA, A));
        Assert.Equal(HttpStatusCode.OK, (await AcceptAsync(Admin(adminB, B), code)).StatusCode);

        var reuse = await AcceptAsync(Admin(adminC, C), code);
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Empty(await Received(Admin(adminC, C)));

        Assert.Equal(HttpStatusCode.BadRequest, (await AcceptAsync(Admin(adminC, C), "AAAAA-AAAAA")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AcceptAsync(Admin(adminC, C), "pas un code")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AcceptAsync(Admin(adminC, C), null)).StatusCode);

        var expired = await InviteAsync(Admin(adminA, A));
        await using (var db = fixture.CreateDbContext())
        {
            await db.ShareInvitations.ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await AcceptAsync(Admin(adminC, C), expired)).StatusCode);
    }

    [Fact]
    public async Task Un_menage_n_accepte_pas_sa_propre_invitation()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var code = await InviteAsync(Admin(adminA, A));

        var response = await AcceptAsync(Admin(Guid.NewGuid(), A), code);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("propre ménage", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Une_seconde_invitation_acceptee_rend_le_meme_partage()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var first = await ShareAToBAsync(adminA, adminB);
        var second = await ShareAToBAsync(adminA, adminB);

        Assert.Equal(first, second);
        Assert.Single(await Received(Admin(adminB, B)));
    }

    [Fact]
    public async Task Le_partage_retire_ou_abandonne_ferme_l_acces()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var shareId = await ShareAToBAsync(adminA, adminB);

        Assert.Equal(HttpStatusCode.NoContent, (await Admin(adminA, A).DeleteAsync($"/sharing/granted/{shareId}")).StatusCode);
        Assert.Empty(await Received(Child(B)));
        Assert.Equal(HttpStatusCode.NotFound, (await Child(B).GetAsync($"/sharing/received/{shareId}/medias")).StatusCode);

        var again = await ShareAToBAsync(adminA, adminB);
        Assert.Equal(HttpStatusCode.NoContent, (await Admin(adminB, B).DeleteAsync($"/sharing/received/{again}")).StatusCode);
        Assert.Empty(await Received(Child(B)));
        Assert.Empty((await Admin(adminA, A).GetFromJsonAsync<List<ShareResponse>>("/sharing/granted", TenantsApiFixture.Json))!);
    }

    [Fact]
    public async Task Un_menage_ne_retire_que_ses_propres_partages()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        await AddHouseholdAsync(C, "Petit");
        var shareId = await ShareAToBAsync(adminA, adminB);

        // C ne peut ni retirer ni abandonner un partage entre A et B ; B ne
        // peut pas le « retirer » comme s'il en était le prêteur.
        await Admin(Guid.NewGuid(), C).DeleteAsync($"/sharing/granted/{shareId}");
        await Admin(Guid.NewGuid(), C).DeleteAsync($"/sharing/received/{shareId}");
        await Admin(adminB, B).DeleteAsync($"/sharing/granted/{shareId}");

        Assert.Single(await Received(Child(B)));
    }

    [Fact]
    public async Task Un_menage_archive_disparait_puis_revient_a_la_restauration()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var shareId = await ShareAToBAsync(adminA, adminB);

        await SetStatusAsync(A, HouseholdStatus.Archived);
        Assert.Empty(await Received(Child(B)));
        Assert.Equal(HttpStatusCode.NotFound, (await Child(B).GetAsync($"/sharing/received/{shareId}/medias")).StatusCode);

        await SetStatusAsync(A, HouseholdStatus.Active);
        Assert.Single(await Received(Child(B)));
    }

    [Fact]
    public async Task La_purge_efface_les_partages_dans_les_deux_sens()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        await AddHouseholdAsync(C, "Petit");
        var adminC = Guid.NewGuid();
        await ShareAToBAsync(adminA, adminB);
        Assert.Equal(HttpStatusCode.OK,
            (await AcceptAsync(Admin(adminA, A), await InviteAsync(Admin(adminB, B)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await AcceptAsync(Admin(adminC, C), await InviteAsync(Admin(adminA, A)))).StatusCode);
        await InviteAsync(Admin(adminB, B));

        await Operator().PostAsync($"/households/{B}/archive", null);
        var purge = await Operator().PostAsJsonAsync($"/households/{B}/purge",
            new PurgeHouseholdRequest(B), TenantsApiFixture.Json);
        Assert.Equal(HttpStatusCode.NoContent, purge.StatusCode);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Shares.AnyAsync(s => s.OwnerHouseholdId == B || s.ViewerHouseholdId == B));
        Assert.False(await db.ShareInvitations.AnyAsync(i => i.OwnerHouseholdId == B));
        // Ce qui ne touche pas B est intact.
        Assert.True(await db.Shares.AnyAsync(s => s.OwnerHouseholdId == A && s.ViewerHouseholdId == C));
    }

    [Fact]
    public async Task Un_enfant_consulte_mais_ne_demande_pas_et_un_adulte_demande_sous_son_nom()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var shareId = await ShareAToBAsync(adminA, adminB);
        var media = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.OK, (await Child(B).GetAsync($"/sharing/received/{shareId}/medias/{media}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Child(B).PostAsync($"/sharing/received/{shareId}/medias/{media}/loan-requests", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Child(B).GetAsync($"/sharing/received/{shareId}/loan-requests")).StatusCode);

        fixture.Sharing.Answer = FakeCatalogueSharing.Ok("""{"id":"x","status":"Requested"}""", 201);
        var adult = Guid.NewGuid();
        var request = await fixture.ClientFor(adult, B, Roles.Adult)
            .PostAsync($"/sharing/received/{shareId}/medias/{media}/loan-requests", null);

        Assert.Equal(HttpStatusCode.Created, request.StatusCode);
        var call = fixture.Sharing.Calls.Last();
        Assert.Equal("POST", call.Method);
        Assert.Equal($"medias/{media}/loan-requests", call.Path);
        var borrower = JsonDocument.Parse(call.Body!).RootElement.GetProperty("BorrowerName").GetString();
        Assert.Equal($"Durand (test-{adult:N})", borrower);
    }

    [Fact]
    public async Task Seul_l_administrateur_invite_accepte_et_retire()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var code = await InviteAsync(Admin(adminA, A));

        Assert.Equal(HttpStatusCode.Forbidden, (await Adult(A).PostAsync("/sharing/invitations", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Adult(A).GetAsync("/sharing/granted")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AcceptAsync(Adult(B), code)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Child(B).DeleteAsync($"/sharing/received/{Guid.NewGuid()}")).StatusCode);

        // L'opérateur, sans ménage, n'a rien à faire ici.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Operator().GetAsync("/sharing/received")).StatusCode);
    }

    [Fact]
    public async Task Les_reponses_du_catalogue_sont_relayees_ou_traduites()
    {
        var (adminA, adminB) = await fixture.ResetAsync();
        var shareId = await ShareAToBAsync(adminA, adminB);
        var url = $"/sharing/received/{shareId}/medias/{Guid.NewGuid()}/loan-requests";

        fixture.Sharing.Answer = FakeCatalogueSharing.Ok("""{"title":"Demande impossible","detail":"Ce média est déjà prêté."}""", 409);
        var conflict = await Admin(adminB, B).PostAsync(url, null);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("déjà prêté", await conflict.Content.ReadAsStringAsync());

        fixture.Sharing.Answer = FakeCatalogueSharing.Ok("", 404);
        Assert.Equal(HttpStatusCode.NotFound, (await Admin(adminB, B).PostAsync(url, null)).StatusCode);

        // Un schéma en retard chez A ne doit pas passer pour celui de B.
        fixture.Sharing.Answer = FakeCatalogueSharing.Ok("""{"type":"https://cataloguemaison/errors/schema-en-retard"}""", 503);
        var lagging = await Admin(adminB, B).PostAsync(url, null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lagging.StatusCode);
        Assert.Contains("catalogue-partage-indisponible", await lagging.Content.ReadAsStringAsync());
        Assert.DoesNotContain("schema-en-retard", await lagging.Content.ReadAsStringAsync());

        fixture.Sharing.Unreachable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Admin(adminB, B).PostAsync(url, null)).StatusCode);
    }

    [Fact]
    public async Task Les_invitations_en_attente_sont_bornees_et_s_annulent()
    {
        var (adminA, _) = await fixture.ResetAsync();
        var admin = Admin(adminA, A);

        for (var i = 0; i < SharingService.MaxPendingInvitations; i++)
        {
            await InviteAsync(admin);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/sharing/invitations", null)).StatusCode);

        var pending = await admin.GetFromJsonAsync<List<InvitationResponse>>("/sharing/invitations", TenantsApiFixture.Json);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/sharing/invitations/{pending![0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync("/sharing/invitations", null)).StatusCode);
    }

    [Fact]
    public async Task Les_essais_de_codes_a_la_chaine_sont_freines()
    {
        await fixture.ResetAsync();
        var guesser = Admin(Guid.NewGuid(), B);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            statuses.Add((await AcceptAsync(guesser, "AAAAA-AAAAA")).StatusCode);
        }

        Assert.All(statuses.Take(5), s => Assert.Equal(HttpStatusCode.BadRequest, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[5]);
    }
}
