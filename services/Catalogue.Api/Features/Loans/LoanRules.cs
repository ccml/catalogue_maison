using Catalogue.Api.Domain;
using Catalogue.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogue.Api.Features.Loans;

/// <summary>
/// Règles de prêt qui portent sur plusieurs lignes, partagées par tous les
/// chemins qui prêtent un média — acceptation d'une demande ou prêt saisi
/// directement — afin qu'ils ne divergent pas.
/// </summary>
internal static class LoanRules
{
    /// <summary>
    /// Le média vient d'être prêté : ses demandes encore en attente ne peuvent
    /// plus être satisfaites, elles sont refusées.
    /// </summary>
    public static async Task SupersedePendingAsync(
        CatalogueDbContext db,
        Guid mediaId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = await db.Loans
            .Where(l => l.MediaId == mediaId && l.Status == LoanStatus.Requested)
            .ToListAsync(cancellationToken);

        foreach (var loan in pending)
        {
            loan.Supersede(now);
        }
    }

    /// <summary>
    /// Deux prêts en cours pour un même média, refusés par l'index unique :
    /// une acceptation concurrente est passée la première.
    /// </summary>
    public static bool IsSecondActiveLoan(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == "ix_loans_media_id_active";
}
