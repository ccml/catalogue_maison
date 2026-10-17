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
    public void La_BnF_donne_la_serie_et_le_tome_d_un_roman()
    {
        var record = BnfSource.Parse(Fixture("bnf-harry-potter-3.xml"), Code("9782070541300"));

        Assert.NotNull(record);
        Assert.Equal(MetadataKind.Book, record.Kind);
        Assert.Equal("Harry Potter et le prisonnier d'Azkaban", record.Title);

        // La notice écrit « Harry Potter. » : la ponctuation ISBD est retirée.
        Assert.Equal("Harry Potter", record.Series);
        Assert.Equal(3, record.Volume);
        Assert.Equal("Roman", record.Genre);
        Assert.Equal(["J. K. Rowling"], record.Creators);
    }

    [Fact]
    public void La_BnF_donne_la_collection_et_son_numero_sans_la_prendre_pour_une_serie()
    {
        var record = BnfSource.Parse(Fixture("bnf-facteur-fatal.xml"), Code("2070408272"));

        Assert.Equal("Folio. Policier", record!.Collection);
        Assert.Equal("85", record.CollectionNumber);
        Assert.Null(record.Series);
        Assert.Equal("Roman", record.Genre);
    }

    [Fact]
    public void De_plusieurs_collections_la_plus_precise_est_retenue()
    {
        // 1984 en Folio porte deux 410 : « Folio. Science-fiction », puis
        // « Collection Folio ». La sous-collection vient en premier.
        var record = BnfSource.Parse(Fixture("bnf-1984-folio.xml"), Code("9782072898884"));

        Assert.Equal("Folio. Science-fiction", record!.Collection);
        Assert.Null(record.CollectionNumber);
    }

    [Fact]
    public void L_editeur_n_est_pas_confondu_avec_l_imprimeur()
    {
        // Les notices récentes portent deux 214 : la publication (Gallimard)
        // et la fabrication (l'imprimeur). Seule la première est l'éditeur.
        var record = BnfSource.Parse(Fixture("bnf-1984-folio.xml"), Code("9782072898884"));

        Assert.Equal("Gallimard", record!.Publisher);
        Assert.Equal(2020, record.Year);
        Assert.Equal(407, record.Pages);
    }

    [Fact]
    public void Le_complement_du_titre_donne_le_genre_quand_c_en_est_un()
    {
        // « L'anomalie : roman » — aucune zone 608 pour un livre adulte, mais
        // le complément du titre dit le genre.
        var record = BnfSource.Parse(Fixture("bnf-anomalie.xml"), Code("9782072895098"));

        Assert.Equal("L'anomalie", record!.Title);
        Assert.Equal("Roman", record.Genre);
        Assert.Equal("Gallimard", record.Publisher);
    }

    [Theory]
    [InlineData(new string[0], "souvenirs d'enfance", null)]
    [InlineData(new string[0], "nouvelles", "Nouvelles")]
    [InlineData(new[] { "Contes" }, null, "Conte")]
    [InlineData(new[] { "Albums", "Bandes dessinées" }, null, "Album")]
    [InlineData(new[] { "Ouvrages illustrés" }, "roman", "Roman")]
    public void Le_genre_n_est_retenu_que_s_il_est_connu(string[] formes, string? complement, string? attendu)
    {
        // Un sous-titre quelconque ne doit pas devenir un genre.
        Assert.Equal(attendu, BnfSource.GenreOf(formes, complement));
    }

    [Fact]
    public void Une_BD_sans_genre_608_est_reconnue_par_la_forme_de_son_contenu()
    {
        // Le cas qui a révélé le défaut : Astérix n'a pas de zone 608 (elle
        // ne vient que des avis du CNLJ), et passait pour un livre.
        var record = BnfSource.Parse(Fixture("bnf-asterix-chez-les-belges.xml"), Code("9782014001129"));

        Assert.Equal(MetadataKind.ComicBook, record!.Kind);
        Assert.Equal("Astérix chez les Belges", record.Title);
        Assert.Equal("Astérix", record.Series);
        Assert.Equal(["René Goscinny"], record.Creators);
        Assert.Equal("Albert Uderzo", record.Illustrator);
        Assert.Equal("Éd. de luxe", record.Edition);
        Assert.Equal("Hachette", record.Publisher);
    }

    [Fact]
    public void Un_manga_donne_sa_serie_sa_collection_et_son_edition()
    {
        var record = BnfSource.Parse(Fixture("bnf-one-piece-1.xml"), Code("9782723488525"));

        Assert.Equal(MetadataKind.ComicBook, record!.Kind);
        Assert.Equal("Romance dawn", record.Title);
        Assert.Equal("One piece", record.Series);
        Assert.Equal(1, record.Volume);
        Assert.Equal("Shonen manga", record.Collection);
        Assert.Equal("Nouvelle éd.", record.Edition);
        Assert.Equal("Glénat", record.Publisher);
    }

    [Fact]
    public void Un_roman_graphique_est_une_BD()
    {
        var record = BnfSource.Parse(Fixture("bnf-blast-1.xml"), Code("9782205063974"));

        Assert.Equal(MetadataKind.ComicBook, record!.Kind);
        Assert.Equal("Grasse carcasse", record.Title);
        Assert.Equal("Blast", record.Series);
        Assert.Equal(1, record.Volume);
        Assert.Null(record.Edition);
    }

    [Theory]
    // Relevés réels : forme du contenu (105 $a), genres (608), indice (686).
    [InlineData("||||t   00|a|", new string[0], null, true)]                    // Astérix, Tintin, Gaston
    [InlineData("a   t   00|a|", new string[0], "823", true)]                   // Lucky Luke, Maus
    [InlineData("a   z   00|y|", new[] { "Albums", "Bandes dessinées" }, null, true)] // Blacksad 3
    [InlineData("||||z   00|a|", new string[0], "805", true)]                   // indice BD seul
    [InlineData("||||z   00|a|", new string[0], "803", false)]                  // roman
    [InlineData("||||z   00|a|", new string[0], "809", false)]                  // album jeunesse
    [InlineData("a   z   00|a|", new string[0], "823", false)]                  // livre illustré
    [InlineData(null, new string[0], null, false)]                               // notice sans données codées
    public void Une_BD_est_reconnue_par_l_un_de_trois_signaux(
        string? codedData, string[] genres, string? classification, bool expected)
    {
        Assert.Equal(expected, BnfSource.IsComic(genres, codedData, [classification]));
    }

    [Fact]
    public void Une_notice_provisoire_est_signalee_et_son_type_n_est_pas_devine()
    {
        // Le cas réel : une BD Glénat parue la veille, décrite d'après
        // l'éditeur. Aucun des trois signaux de BD, pas même un rôle pour le
        // dessinateur : la notice le dit livre, et elle est signalée comme
        // provisoire pour que l'utilisateur vérifie.
        var record = BnfSource.Parse(Fixture("bnf-probleme-trois-corps-provisoire.xml"), Code("9782344075173"));

        Assert.True(record!.Provisional);
        Assert.Equal(MetadataKind.Book, record.Kind);
        Assert.Equal("Le Problème à trois corps - Tome 01", record.Title);
        Assert.Equal("Glénat", record.Publisher);
        Assert.Equal(184, record.Pages);
    }

    [Theory]
    [InlineData("bnf-asterix-chez-les-belges.xml", "9782014001129")]
    [InlineData("bnf-blacksad-3.xml", "9782205059250")]
    [InlineData("bnf-anomalie.xml", "9782072895098")]
    public void Une_notice_cataloguee_n_est_pas_provisoire(string fixture, string code)
    {
        Assert.False(BnfSource.Parse(Fixture(fixture), Code(code))!.Provisional);
    }

    [Fact]
    public void Un_roman_n_a_pas_de_mention_d_edition_inventee()
    {
        Assert.Null(BnfSource.Parse(Fixture("bnf-anomalie.xml"), Code("9782072895098"))!.Edition);
        Assert.Equal(MetadataKind.Book, BnfSource.Parse(Fixture("bnf-anomalie.xml"), Code("9782072895098"))!.Kind);
    }

    [Fact]
    public void Une_BD_n_a_pas_de_genre_ni_de_collection_inventes()
    {
        var record = BnfSource.Parse(Fixture("bnf-blacksad-3.xml"), Code("9782205059250"));

        Assert.Null(record!.Genre);
        Assert.Null(record.Collection);
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
