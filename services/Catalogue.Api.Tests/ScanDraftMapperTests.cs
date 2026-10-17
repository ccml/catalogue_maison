using Catalogue.Api.Domain;
using Catalogue.Api.Features.Scans;
using Catalogue.Api.Infrastructure.Metadata;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Ce que Catalogue.Api conserve d'une notice. Une source externe n'est pas
/// une entrée de confiance : elle passe par les mêmes bornes que la saisie.
/// </summary>
public sealed class ScanDraftMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Un_livre_garde_son_ISBN_son_editeur_et_ses_pages()
    {
        var draft = ScanDraftMapper.FromRecord(new MetadataRecordDto
        {
            Kind = "Book",
            Title = "  Le facteur fatal ",
            Creators = ["Didier Daeninckx"],
            Year = 1999,
            Publisher = "Gallimard",
            Pages = 201,
            Isbn = "978-2-07-040827-6"
        }, "9782070408276", Now);

        Assert.NotNull(draft);
        Assert.Equal(MediaType.Book, draft.Type);
        Assert.Equal("Le facteur fatal", draft.Title);
        var book = Assert.IsType<BookSpecifics>(draft.Specifics);
        Assert.Equal("9782070408276", book.Isbn);
        Assert.Equal("Gallimard", book.Publisher);
        Assert.Equal(201, book.Pages);
    }

    [Fact]
    public void Plusieurs_auteurs_partagent_la_colonne_createur()
    {
        var draft = ScanDraftMapper.FromRecord(new MetadataRecordDto
        {
            Kind = "Book",
            Title = "Good Omens",
            Creators = ["Terry Pratchett", "Neil Gaiman", "Terry Pratchett", " "]
        }, "9780552137034", Now);

        Assert.Equal("Terry Pratchett, Neil Gaiman", draft!.Creator);
    }

    [Fact]
    public void Une_notice_sans_ISBN_garde_celui_qui_a_ete_scanne()
    {
        var draft = ScanDraftMapper.FromRecord(
            new MetadataRecordDto { Kind = "ComicBook", Title = "Ame rouge", Series = "Blacksad" },
            "9782205059250",
            Now);

        Assert.Equal("9782205059250", Assert.IsType<ComicBookSpecifics>(draft!.Specifics).Isbn);
    }

    [Fact]
    public void Un_album_garde_son_label_son_support_et_ses_pistes()
    {
        var draft = ScanDraftMapper.FromRecord(new MetadataRecordDto
        {
            Kind = "Music",
            Title = "Homework",
            Creators = ["Daft Punk"],
            Label = "Virgin",
            Format = "CD",
            Tracks = ["Daftendirekt", "", "Revolution 909"]
        }, "0724384260958", Now);

        Assert.Equal(MediaType.Music, draft!.Type);
        var music = Assert.IsType<MusicSpecifics>(draft.Specifics);
        Assert.Equal("Virgin", music.Label);
        Assert.Equal(["Daftendirekt", "Revolution 909"], music.Tracks);
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(2031)]
    public void Une_annee_implausible_est_ecartee(int year)
    {
        var draft = ScanDraftMapper.FromRecord(
            new MetadataRecordDto { Kind = "Book", Title = "Titre", Year = year }, "9782070408276", Now);

        Assert.Null(draft!.Year);
    }

    [Fact]
    public void Des_valeurs_que_la_saisie_refuserait_ne_sont_pas_proposees()
    {
        var draft = ScanDraftMapper.FromRecord(new MetadataRecordDto
        {
            Kind = "ComicBook",
            Title = new string('A', 600),
            Volume = 0,
            Pages = -3
        }, "9782205059250", Now);

        Assert.Equal(512, draft!.Title!.Length);
        Assert.Null(Assert.IsType<ComicBookSpecifics>(draft.Specifics).Volume);
    }

    [Theory]
    [InlineData("Film")]
    [InlineData("")]
    public void Une_notice_d_un_type_inconnu_ne_donne_pas_de_fiche(string kind)
    {
        Assert.Null(ScanDraftMapper.FromRecord(
            new MetadataRecordDto { Kind = kind, Title = "Titre" }, "3700000000001", Now));
    }

    [Fact]
    public void Une_notice_sans_titre_ne_donne_pas_de_fiche()
    {
        Assert.Null(ScanDraftMapper.FromRecord(
            new MetadataRecordDto { Kind = "Book", Title = "  " }, "9782070408276", Now));
    }

    [Fact]
    public void Sans_notice_un_ISBN_suffit_a_proposer_un_livre()
    {
        var draft = ScanDraftMapper.FromCodeOnly("9782070408276");

        Assert.Equal(MediaType.Book, draft!.Type);
        Assert.Null(draft.Title);
        Assert.Equal("9782070408276", Assert.IsType<BookSpecifics>(draft.Specifics).Isbn);
    }

    [Fact]
    public void Sans_notice_un_code_barres_commercial_ne_dit_rien_du_type()
    {
        Assert.Null(ScanDraftMapper.FromCodeOnly("0724384260958"));
    }

    [Theory]
    [InlineData("2-07-040827-2", new[] { "2070408272", "9782070408276" })]
    [InlineData("9782070408276", new[] { "9782070408276", "2070408272" })]
    [InlineData("080442957X", new[] { "080442957X", "9780804429573" })]
    [InlineData("9791090636071", new[] { "9791090636071" })]
    public void Un_ISBN_10_et_son_ISBN_13_sont_equivalents(string raw, string[] expected)
    {
        Assert.Equal(expected, IsbnFormat.Equivalents(raw));
    }
}
