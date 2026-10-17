using Metadata.Api.Domain;
using Metadata.Api.Infrastructure.Sources;
using Xunit;

namespace Metadata.Api.Tests;

/// <summary>
/// Lecture des réponses réelles des sources, figées dans <c>Fixtures/</c>.
/// Une source qui change son format fera échouer la lecture en production :
/// ces tests disent précisément ce qu'on en attend.
/// </summary>
public sealed class SourceParsingTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static ProductCode Code(string raw) =>
        ProductCode.TryParse(raw, out var code) ? code! : throw new ArgumentException(raw);

    [Fact]
    public void La_BnF_distingue_serie_tome_scenariste_et_dessinateur_d_une_BD()
    {
        var record = BnfSource.Parse(Fixture("bnf-blacksad-3.xml"), Code("9782205059250"));

        Assert.NotNull(record);
        Assert.Equal(MetadataKind.ComicBook, record.Kind);
        Assert.Equal("Ame rouge", record.Title);
        Assert.Equal("Blacksad", record.Series);
        Assert.Equal(3, record.Volume);
        Assert.Equal(["Juan Díaz Canales"], record.Creators);
        Assert.Equal("Juanjo Guarnido", record.Illustrator);
        Assert.Equal("Dargaud", record.Publisher);
        Assert.Equal(2006, record.Year);
        Assert.Equal(100, record.Pages);
        Assert.Equal("9782205059250", record.Isbn);
        Assert.Equal(BnfSource.SourceName, record.Source);
    }

    [Fact]
    public void La_BnF_decrit_un_livre_sans_serie_ni_dessinateur()
    {
        var record = BnfSource.Parse(Fixture("bnf-facteur-fatal.xml"), Code("2070408272"));

        Assert.NotNull(record);
        Assert.Equal(MetadataKind.Book, record.Kind);
        Assert.Equal("Le facteur fatal", record.Title);
        Assert.Equal(["Didier Daeninckx"], record.Creators);
        Assert.Equal("Gallimard", record.Publisher);
        Assert.Equal(1999, record.Year);
        Assert.Equal(201, record.Pages);

        // La collection (« Folio policier ») n'est pas une série : elle ne
        // doit pas être prise pour telle.
        Assert.Null(record.Series);
        Assert.Null(record.Volume);
        Assert.Null(record.Illustrator);
    }

    [Fact]
    public void Une_reponse_BnF_sans_notice_signifie_code_inconnu()
    {
        Assert.Null(BnfSource.Parse(Fixture("bnf-aucune-notice.xml"), Code("9780000000002")));
    }

    [Theory]
    [InlineData("<html>Maintenance</html>")]
    [InlineData("pas du XML")]
    public void Une_reponse_BnF_illisible_est_une_panne_et_non_un_code_inconnu(string body)
    {
        // La distinction compte : un code inconnu est mis en cache, une panne
        // ne doit jamais l'être.
        Assert.Throws<MetadataSourceException>(() => BnfSource.Parse(body, Code("9782205059250")));
    }

    [Fact]
    public void Open_Library_decrit_un_livre()
    {
        var record = OpenLibrarySource.Parse(Fixture("openlibrary-facteur-fatal.json"), Code("9782070408276"));

        Assert.NotNull(record);
        Assert.Equal(MetadataKind.Book, record.Kind);
        Assert.Equal("Factuer Fatal", record.Title);
        Assert.Equal(["Daeninckx"], record.Creators);
        Assert.Equal("Editions Flammarion", record.Publisher);
        Assert.Equal(1998, record.Year);
        Assert.Equal(201, record.Pages);
        Assert.Equal(OpenLibrarySource.SourceName, record.Source);
    }

    [Fact]
    public void Open_Library_repond_un_objet_vide_pour_un_code_inconnu()
    {
        Assert.Null(OpenLibrarySource.Parse("{}", Code("9782070408276")));
    }

    [Fact]
    public void MusicBrainz_retient_une_edition_dont_le_code_correspond()
    {
        var id = MusicBrainzSource.PickRelease(Fixture("musicbrainz-recherche-homework.json"), "0724384260958");

        Assert.Equal("b314ce03-0753-4f86-9a66-ec0eff166083", id);
    }

    [Fact]
    public void MusicBrainz_prefere_un_support_physique_a_une_edition_numerique()
    {
        const string body = """
            {"releases":[
              {"id":"numerique","barcode":"724384260958","media":[{"format":"Digital Media"}]},
              {"id":"cd","barcode":"0724384260958","media":[{"format":"CD"}]},
              {"id":"autre-disque","barcode":"5099999999999","media":[{"format":"CD"}]}
            ]}
            """;

        Assert.Equal("cd", MusicBrainzSource.PickRelease(body, "724384260958"));
    }

    [Fact]
    public void MusicBrainz_ignore_un_resultat_dont_le_code_differe()
    {
        // La recherche plein texte ramène des voisins : sans code identique,
        // ce n'est pas le disque qu'on a dans les mains.
        const string body = """{"releases":[{"id":"voisin","barcode":"0724384260959","media":[{"format":"CD"}]}]}""";

        Assert.Null(MusicBrainzSource.PickRelease(body, "0724384260958"));
    }

    [Fact]
    public void MusicBrainz_decrit_un_album_et_ses_pistes()
    {
        var record = MusicBrainzSource.ParseRelease(Fixture("musicbrainz-edition-homework.json"));

        Assert.NotNull(record);
        Assert.Equal(MetadataKind.Music, record.Kind);
        Assert.Equal("Homework", record.Title);
        Assert.Equal(["Daft Punk"], record.Creators);
        Assert.Equal("ADA France", record.Label);
        Assert.Equal(16, record.Tracks.Count);
        Assert.Equal("Daftendirekt", record.Tracks[0]);
        Assert.Equal(MusicBrainzSource.SourceName, record.Source);
    }

    [Theory]
    [InlineData("724384260958", new[] { "724384260958", "0724384260958" })]
    [InlineData("0724384260958", new[] { "0724384260958", "724384260958" })]
    [InlineData("5099902987620", new[] { "5099902987620" })]
    public void Un_UPC_et_son_EAN_prefixe_de_zero_designent_le_meme_disque(string code, string[] expected)
    {
        Assert.Equal(expected, MusicBrainzSource.BarcodeVariants(code));
    }

    [Theory]
    [InlineData("December 31, 1998", 1998)]
    [InlineData("1999-08-26", 1999)]
    [InlineData("[2006]", 2006)]
    [InlineData("12345", null)]
    [InlineData("s.d.", null)]
    public void L_annee_est_lue_dans_les_formes_usuelles(string raw, int? expected)
    {
        Assert.Equal(expected, SourceHttp.ParseYear(raw));
    }
}
