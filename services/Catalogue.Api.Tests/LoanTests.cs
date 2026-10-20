using Catalogue.Api.Domain;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Transitions d'un prêt, et leur effet sur <see cref="Media.IsLent"/> : il
/// doit être vrai exactement quand un prêt est en cours.
/// </summary>
public sealed class LoanTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Partage = Guid.NewGuid();

    private static Media UnFilm() => Media.Create(MediaType.Film, "Alien", null, 1979, "Salon", null, Maintenant);

    [Fact]
    public void Un_pret_saisi_par_le_menage_part_tout_de_suite()
    {
        var media = UnFilm();

        var pret = Loan.Lend(media, "  Tante Jeanne ", Maintenant);

        Assert.Equal(LoanStatus.Active, pret.Status);
        Assert.Equal("Tante Jeanne", pret.BorrowerName);
        Assert.Equal(Maintenant, pret.LentAt);
        Assert.Null(pret.ShareId);
        Assert.True(media.IsLent);
    }

    [Fact]
    public void Sans_nom_l_emprunteur_est_inconnu()
    {
        var pret = Loan.Lend(UnFilm(), "   ", Maintenant);

        Assert.Equal(Loan.UnknownBorrower, pret.BorrowerName);
    }

    [Fact]
    public void Un_nom_trop_long_est_refuse()
    {
        Assert.Throws<LoanException>(() => Loan.Lend(UnFilm(), new string('x', Loan.BorrowerNameMaxLength + 1), Maintenant));
    }

    [Fact]
    public void Un_media_deja_prete_ne_se_prete_ni_ne_se_demande()
    {
        var media = UnFilm();
        Loan.Lend(media, "Tante Jeanne", Maintenant);

        Assert.Throws<LoanException>(() => Loan.Lend(media, "Oncle Paul", Maintenant));
        Assert.Throws<LoanException>(() => Loan.Request(media, Partage, "Famille Durand (paul)", Maintenant));
    }

    [Fact]
    public void Une_demande_ne_fait_pas_partir_le_media()
    {
        var media = UnFilm();

        var demande = Loan.Request(media, Partage, "Famille Durand (paul)", Maintenant);

        Assert.Equal(LoanStatus.Requested, demande.Status);
        Assert.Equal(Partage, demande.ShareId);
        Assert.Equal(Maintenant, demande.RequestedAt);
        Assert.Null(demande.LentAt);
        Assert.False(media.IsLent);
    }

    [Fact]
    public void Accepter_une_demande_prete_le_media_et_rendre_le_fait_revenir()
    {
        var media = UnFilm();
        var demande = Loan.Request(media, Partage, "Famille Durand (paul)", Maintenant);

        demande.Accept(media, Maintenant.AddHours(1));

        Assert.Equal(LoanStatus.Active, demande.Status);
        Assert.Equal(Maintenant.AddHours(1), demande.LentAt);
        Assert.True(media.IsLent);

        demande.Return(media, Maintenant.AddDays(10));

        Assert.Equal(LoanStatus.Returned, demande.Status);
        Assert.Equal(Maintenant.AddDays(10), demande.ClosedAt);
        Assert.False(media.IsLent);
    }

    [Fact]
    public void Une_demande_ne_s_accepte_pas_si_le_media_est_deja_prete()
    {
        var media = UnFilm();
        var premiere = Loan.Request(media, Partage, "Famille Durand (paul)", Maintenant);
        var seconde = Loan.Request(media, Guid.NewGuid(), "Famille Petit (lea)", Maintenant);

        premiere.Accept(media, Maintenant);

        Assert.Throws<LoanException>(() => seconde.Accept(media, Maintenant));
    }

    [Fact]
    public void Une_demande_devenue_sans_objet_est_refusee()
    {
        var demande = Loan.Request(UnFilm(), Partage, "Famille Durand (paul)", Maintenant);

        demande.Supersede(Maintenant);

        Assert.Equal(LoanStatus.Declined, demande.Status);
        Assert.Equal(Maintenant, demande.ClosedAt);
    }

    [Fact]
    public void Remplacer_ne_touche_pas_un_pret_en_cours()
    {
        var pret = Loan.Lend(UnFilm(), "Tante Jeanne", Maintenant);

        pret.Supersede(Maintenant);

        Assert.Equal(LoanStatus.Active, pret.Status);
    }

    [Fact]
    public void Seule_une_demande_en_attente_se_refuse_ou_se_retire()
    {
        var media = UnFilm();
        var pret = Loan.Lend(media, "Tante Jeanne", Maintenant);

        Assert.Throws<LoanException>(() => pret.Decline(Maintenant));
        Assert.Throws<LoanException>(() => pret.Cancel(Maintenant));

        var demande = Loan.Request(UnFilm(), Partage, "Famille Durand (paul)", Maintenant);
        demande.Cancel(Maintenant);

        Assert.Equal(LoanStatus.Cancelled, demande.Status);
        Assert.Throws<LoanException>(() => demande.Decline(Maintenant));
    }

    [Fact]
    public void Seul_un_pret_en_cours_se_rend()
    {
        var media = UnFilm();
        var demande = Loan.Request(media, Partage, "Famille Durand (paul)", Maintenant);

        Assert.Throws<LoanException>(() => demande.Return(media, Maintenant));
        Assert.False(media.IsLent);
    }

    [Fact]
    public void Un_pret_ne_s_applique_pas_a_un_autre_media()
    {
        var demande = Loan.Request(UnFilm(), Partage, "Famille Durand (paul)", Maintenant);

        Assert.Throws<ArgumentException>(() => demande.Accept(UnFilm(), Maintenant));
    }
}
