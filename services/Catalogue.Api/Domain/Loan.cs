namespace Catalogue.Api.Domain;

/// <summary>
/// Où en est un prêt. Une demande venue d'un ménage partenaire commence
/// <see cref="Requested"/> ; un prêt saisi par le ménage lui-même commence
/// directement <see cref="Active"/>.
/// </summary>
public enum LoanStatus
{
    /// <summary>Demandé par un ménage partenaire, en attente de réponse.</summary>
    Requested,

    /// <summary>Le média est chez l'emprunteur. Au plus un par média.</summary>
    Active,

    /// <summary>Le média est revenu.</summary>
    Returned,

    /// <summary>Demande refusée par le ménage, ou devenue sans objet parce que le média a été prêté à un autre.</summary>
    Declined,

    /// <summary>Demande retirée par le ménage qui l'avait faite.</summary>
    Cancelled
}

/// <summary>
/// Un prêt d'un média du ménage, ou une demande de prêt. Une ligne par prêt :
/// l'historique est conservé.
/// <para>
/// L'emprunteur est un <strong>libellé copié</strong> au moment du prêt, et
/// jamais l'identifiant d'un autre ménage : ce schéma ne contient rien qui
/// désigne un autre ménage. Une demande venue d'un partage porte en plus
/// l'identifiant opaque de ce partage, attribué par Tenants.Api.
/// </para>
/// <para>
/// <see cref="Media.IsLent"/> reste une colonne réelle — elle sert au filtre
/// et à l'export —, mais elle n'est écrite que par les transitions
/// ci-dessous : elle est vraie exactement quand un prêt est
/// <see cref="LoanStatus.Active"/>.
/// </para>
/// </summary>
public class Loan
{
    public const int BorrowerNameMaxLength = 200;

    /// <summary>Emprunteur d'un prêt dont on ne sait rien : antérieur au suivi des prêts, ou importé.</summary>
    public const string UnknownBorrower = "Inconnu";

    private Loan()
    {
        // Réservé à EF Core.
        BorrowerName = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid MediaId { get; private set; }

    public LoanStatus Status { get; private set; }

    /// <summary>Partage par lequel la demande est arrivée ; absent pour un prêt saisi par le ménage.</summary>
    public Guid? ShareId { get; private set; }

    public string BorrowerName { get; private set; }

    public DateTimeOffset? RequestedAt { get; private set; }

    public DateTimeOffset? LentAt { get; private set; }

    /// <summary>Retour, refus ou retrait : la fin du prêt ou de la demande.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Prêt saisi par le ménage lui-même : le média part tout de suite.</summary>
    /// <exception cref="LoanException">Si le média est déjà prêté ou le nom invalide.</exception>
    public static Loan Lend(Media media, string? borrowerName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(media);

        if (media.IsLent)
        {
            throw new LoanException("Ce média est déjà prêté.");
        }

        var loan = new Loan
        {
            Id = Guid.CreateVersion7(),
            MediaId = media.Id,
            Status = LoanStatus.Active,
            BorrowerName = CleanName(borrowerName),
            LentAt = now
        };

        media.MarkLent();
        return loan;
    }

    /// <summary>Demande d'un ménage partenaire. Le média ne bouge pas tant qu'elle n'est pas acceptée.</summary>
    /// <exception cref="LoanException">Si le média est déjà prêté ou le nom invalide.</exception>
    public static Loan Request(Media media, Guid shareId, string? borrowerName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(media);

        if (shareId == Guid.Empty)
        {
            throw new ArgumentException("Partage requis.", nameof(shareId));
        }

        if (media.IsLent)
        {
            throw new LoanException("Ce média est déjà prêté.");
        }

        return new Loan
        {
            Id = Guid.CreateVersion7(),
            MediaId = media.Id,
            Status = LoanStatus.Requested,
            ShareId = shareId,
            BorrowerName = CleanName(borrowerName),
            RequestedAt = now
        };
    }

    /// <summary>
    /// Accepte la demande : le média part. Les autres demandes en attente sur
    /// ce média sont à refuser par l'appelant (<see cref="Supersede"/>) :
    /// elles ne peuvent plus être satisfaites.
    /// </summary>
    /// <exception cref="LoanException">Si la demande n'est plus en attente ou le média déjà prêté.</exception>
    public void Accept(Media media, DateTimeOffset now)
    {
        RequireMedia(media);
        RequireStatus(LoanStatus.Requested, "Seule une demande en attente peut être acceptée.");

        if (media.IsLent)
        {
            throw new LoanException("Ce média est déjà prêté.");
        }

        Status = LoanStatus.Active;
        LentAt = now;
        media.MarkLent();
    }

    /// <exception cref="LoanException">Si la demande n'est plus en attente.</exception>
    public void Decline(DateTimeOffset now)
    {
        RequireStatus(LoanStatus.Requested, "Seule une demande en attente peut être refusée.");
        Status = LoanStatus.Declined;
        ClosedAt = now;
    }

    /// <summary>Retrait de la demande par le ménage qui l'avait faite.</summary>
    /// <exception cref="LoanException">Si la demande n'est plus en attente.</exception>
    public void Cancel(DateTimeOffset now)
    {
        RequireStatus(LoanStatus.Requested, "Seule une demande en attente peut être retirée.");
        Status = LoanStatus.Cancelled;
        ClosedAt = now;
    }

    /// <summary>Le média a été prêté par un autre chemin : la demande devient sans objet.</summary>
    public void Supersede(DateTimeOffset now)
    {
        if (Status == LoanStatus.Requested)
        {
            Status = LoanStatus.Declined;
            ClosedAt = now;
        }
    }

    /// <exception cref="LoanException">Si le prêt n'est pas en cours.</exception>
    public void Return(Media media, DateTimeOffset now)
    {
        RequireMedia(media);
        RequireStatus(LoanStatus.Active, "Seul un prêt en cours peut être rendu.");

        Status = LoanStatus.Returned;
        ClosedAt = now;
        media.MarkReturned();
    }

    private void RequireMedia(Media media)
    {
        ArgumentNullException.ThrowIfNull(media);

        if (media.Id != MediaId)
        {
            throw new ArgumentException("Ce prêt ne concerne pas ce média.", nameof(media));
        }
    }

    private void RequireStatus(LoanStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new LoanException(message);
        }
    }

    private static string CleanName(string? borrowerName)
    {
        var name = borrowerName?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            return UnknownBorrower;
        }

        if (name.Length > BorrowerNameMaxLength)
        {
            throw new LoanException($"Le nom de l'emprunteur ne peut pas dépasser {BorrowerNameMaxLength} caractères.");
        }

        return name;
    }
}

/// <summary>Transition de prêt refusée : l'état ne le permet pas.</summary>
public sealed class LoanException(string message) : Exception(message);
