using System.Net;
using System.Net.Http.Json;
using Catalogue.Api.Domain;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Features.Scans;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Metadata;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Le code-barres d'un disque, clé de ses doublons au scan comme l'ISBN pour
/// un livre.
/// </summary>
public sealed class BarcodeFormatTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("724384260958", "0724384260958")]
    [InlineData("0724384260958", "0724384260958")]
    [InlineData("0 724384 260958", "0724384260958")]
    [InlineData("96385074", "96385074")]
    public void Un_UPC_et_son_EAN_13_ont_la_meme_forme_canonique(string raw, string expected)
    {
        Assert.Equal(expected, BarcodeFormat.Normalize(raw));
        Assert.True(BarcodeFormat.IsWellFormed(raw));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("07243842609A8")]
    [InlineData("07243842609581")]
    public void Un_code_barres_mal_forme_est_refuse_a_l_entree(string raw)
    {
        var exception = Assert.Throws<MediaValidationException>(() => Media.Create(
            MediaType.Music, "Homework", null, null, null, new MusicSpecifics { Barcode = raw }, Maintenant));

        Assert.Contains("Le code-barres doit comporter 8, 12 ou 13 chiffres.", exception.Errors);
    }

    [Fact]
    public void Le_code_barres_est_stocke_sous_sa_forme_canonique()
    {
        var media = Media.Create(
            MediaType.Music, "Homework", "Daft Punk", 1997, null,
            new MusicSpecifics { Barcode = "724384260958" }, Maintenant);

        Assert.Equal("0724384260958", Assert.IsType<MusicSpecifics>(media.Specifics).Barcode);
    }

    [Fact]
    public void La_fiche_d_un_disque_scanne_garde_son_code_barres()
    {
        var draft = ScanDraftMapper.FromRecord(
            new MetadataRecordDto { Kind = "Music", Title = "Homework", Format = "CD" },
            "724384260958",
            Maintenant);

        Assert.Equal("0724384260958", Assert.IsType<MusicSpecifics>(draft!.Specifics).Barcode);
    }
}

public sealed class BarcodeScanTests(CatalogueApiFixture fixture) : IClassFixture<CatalogueApiFixture>
{
    private static MetadataLookup Homework() => new(MetadataLookupOutcome.Found, new MetadataRecordDto
    {
        Kind = "Music",
        Title = "Homework",
        Creators = ["Daft Punk"],
        Label = "Virgin",
        Format = "CD",
        Tracks = ["Daftendirekt", "Revolution 909"],
        Source = "musicbrainz"
    });

    [Fact]
    public async Task Un_disque_enregistre_depuis_un_scan_est_reconnu_au_scan_suivant()
    {
        fixture.Metadata.Answer = _ => Homework();
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);

        // Scan par l'UPC-A de 12 chiffres, puis enregistrement tel que proposé.
        var draft = (await client.GetFromJsonAsync<ScanResponse>("/scans/724384260958", CatalogueApiFixture.Json))!.Draft!;
        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            draft.Type, draft.Title, draft.Creator, draft.Year, "Salon", draft.Specifics), CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        // Rescanné sous sa forme EAN-13 : même disque, reconnu, et
        // Metadata.Api n'est plus sollicité.
        var before = fixture.Metadata.Requested.Count;
        var rescan = await client.GetFromJsonAsync<ScanResponse>("/scans/0724384260958", CatalogueApiFixture.Json);

        Assert.Equal(EnrichmentStatus.NotAttempted, rescan!.Enrichment);
        Assert.Equal("Homework", rescan.Existing?.Title);
        Assert.Null(rescan.Draft);
        Assert.Equal(before, fixture.Metadata.Requested.Count);
    }

    [Fact]
    public async Task Un_disque_saisi_a_la_main_avec_son_code_barres_est_reconnu_au_scan()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Music, "Kind of Blue", "Miles Davis", 1959,
            Specifics: new MusicSpecifics { Barcode = "5099702798320" }), CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        var scan = await client.GetFromJsonAsync<ScanResponse>("/scans/5099702798320", CatalogueApiFixture.Json);

        Assert.Equal("Kind of Blue", scan!.Existing?.Title);
    }

    [Fact]
    public async Task Un_disque_d_un_autre_menage_n_est_pas_un_doublon()
    {
        // Le garde-fou multitenant, étendu au code-barres : un disque présent
        // chez B n'existe pas pour A, ni en doublon, ni en fiche proposée.
        fixture.Metadata.Answer = _ => MetadataLookup.NotFound;
        var menageB = fixture.ClientFor(CatalogueApiFixture.HouseholdB, Roles.Adult);
        var creation = await menageB.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Music, "Disque de B", Specifics: new MusicSpecifics { Barcode = "3700000000124" }),
            CatalogueApiFixture.Json);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        var menageA = fixture.ClientFor(CatalogueApiFixture.HouseholdA, Roles.Adult);
        var scanA = await menageA.GetFromJsonAsync<ScanResponse>("/scans/3700000000124", CatalogueApiFixture.Json);

        Assert.Null(scanA!.Existing);
        Assert.Null(scanA.Draft);
        Assert.Equal(EnrichmentStatus.NotFound, scanA.Enrichment);

        // Contrôle : chez B, le même scan reconnaît bien le disque.
        var scanB = await menageB.GetFromJsonAsync<ScanResponse>("/scans/3700000000124", CatalogueApiFixture.Json);
        Assert.Equal("Disque de B", scanB!.Existing?.Title);
    }

    [Fact]
    public async Task Corriger_un_disque_conserve_son_code_barres()
    {
        var client = fixture.ClientFor(CatalogueApiFixture.HouseholdB, Roles.Adult);
        var creation = await client.PostAsJsonAsync("/medias", new CreateMediaRequest(
            MediaType.Music, "Discovery", Specifics: new MusicSpecifics { Barcode = "724384960650" }),
            CatalogueApiFixture.Json);
        var cree = await creation.Content.ReadFromJsonAsync<MediaDetailResponse>(CatalogueApiFixture.Json);

        var relu = await client.GetFromJsonAsync<MediaDetailResponse>($"/medias/{cree!.Id}", CatalogueApiFixture.Json);

        Assert.Equal("0724384960650", Assert.IsType<MusicSpecifics>(relu!.Specifics).Barcode);
    }
}
