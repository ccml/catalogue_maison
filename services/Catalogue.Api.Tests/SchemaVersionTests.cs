using System.Net;
using Catalogue.Api.Infrastructure;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Un schéma en retard de migration est un incident : le service doit le
/// refuser franchement, et non échouer au hasard des requêtes selon
/// qu'elles touchent ou non une colonne manquante.
/// </summary>
public sealed class SchemaVersionTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    [Fact]
    public async Task Un_menage_dont_le_schema_n_est_pas_provisionne_est_refuse()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdWithoutSchema, Roles.Adult);

        var reponse = await client.GetAsync("/medias");

        // 503 et non 500 : la demande est légitime, c'est le service qui
        // n'est pas en état de la servir.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_menage_provisionne_est_servi_normalement()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        var reponse = await client.GetAsync("/medias");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Provisionner_un_menage_le_remet_en_service_sans_redemarrage()
    {
        const string menage = "menage_tardif";
        var client = fixture.ClientFor(menage, Roles.Adult);

        var avant = await client.GetAsync("/medias");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, avant.StatusCode);

        // Seuls les contrôles réussis sont mémorisés : appliquer la migration
        // doit suffire, sans vider de cache ni redémarrer le processus.
        await fixture.ProvisionAsync(menage);

        var apres = await client.GetAsync("/medias");
        Assert.Equal(HttpStatusCode.OK, apres.StatusCode);
    }
}
