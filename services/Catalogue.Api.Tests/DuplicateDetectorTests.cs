using Catalogue.Api.Domain;
using Catalogue.Api.Features.Transfer;
using Xunit;

namespace Catalogue.Api.Tests;

public sealed class DuplicateDetectorTests
{
    [Fact]
    public void Un_livre_saisi_avec_l_isbn_10_est_reconnu_par_son_isbn_13()
    {
        var detector = new DuplicateDetector();
        detector.AddExisting(MediaType.Book, "Le Horla", "Maupassant", "2070408272", null);

        var reason = detector.Check(MediaType.Book, "Le Horla", "Maupassant",
            new BookSpecifics { Isbn = "978-2-07-040827-6" });

        Assert.Equal(DuplicateReason.SameIsbn, reason);
    }

    [Fact]
    public void Deux_editions_d_un_meme_livre_ne_sont_pas_des_doublons()
    {
        // Même titre, même auteur, ISBN différents : une édition de poche à
        // côté de l'édition reliée, ce sont bien deux objets.
        var detector = new DuplicateDetector();
        detector.AddExisting(MediaType.Book, "Le Horla", "Maupassant", "9782070408278", null);

        var reason = detector.Check(MediaType.Book, "Le Horla", "Maupassant",
            new BookSpecifics { Isbn = "9782253006572" });

        Assert.Null(reason);
    }

    [Fact]
    public void Un_disque_est_reconnu_par_son_code_barres_UPC_ou_EAN()
    {
        var detector = new DuplicateDetector();
        detector.AddExisting(MediaType.Music, "Kind of Blue", "Miles Davis", null, "0724384260958");

        var reason = detector.Check(MediaType.Music, "Kind of Blue (remaster)", "Miles Davis",
            new MusicSpecifics { Barcode = "724384260958" });

        Assert.Equal(DuplicateReason.SameBarcode, reason);
    }

    [Fact]
    public void Sans_identifiant_le_titre_compare_sans_casse_ni_espaces()
    {
        var detector = new DuplicateDetector();
        detector.AddExisting(MediaType.Game, "Carcassonne", "Klaus-Jürgen Wrede", null, null);

        Assert.Equal(DuplicateReason.SameTitle,
            detector.Check(MediaType.Game, "  carcassonne ", "KLAUS-JÜRGEN WREDE", null));
    }

    [Fact]
    public void Le_meme_titre_d_un_autre_type_n_est_pas_un_doublon()
    {
        // Le livre et son adaptation en film.
        var detector = new DuplicateDetector();
        detector.AddExisting(MediaType.Book, "Dune", "Frank Herbert", null, null);

        Assert.Null(detector.Check(MediaType.Film, "Dune", "Frank Herbert", null));
    }

    [Fact]
    public void Une_fiche_sans_isbn_retrouve_par_son_titre_un_livre_qui_en_a_un()
    {
        var detector = new DuplicateDetector();
        detector.AddExisting(MediaType.Book, "Le Horla", "Maupassant", "9782070408278", null);

        Assert.Equal(DuplicateReason.SameTitle,
            detector.Check(MediaType.Book, "Le Horla", "Maupassant", null));
    }

    [Fact]
    public void Une_fiche_repetee_dans_le_fichier_n_est_importee_qu_une_fois()
    {
        var detector = new DuplicateDetector();

        Assert.Null(detector.Check(MediaType.Film, "Alien", "Ridley Scott", null));
        Assert.Equal(DuplicateReason.RepeatedInFile,
            detector.Check(MediaType.Film, "Alien", "Ridley Scott", null));
    }
}
