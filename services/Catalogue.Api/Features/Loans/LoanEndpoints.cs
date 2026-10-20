using Catalogue.Api.Domain;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Catalogue.Api.Features.Loans;

/// <summary>
/// Les prêts du ménage, vus du côté prêteur : demandes reçues des ménages
/// partenaires et prêts en cours. Tout membre les voit ; seul qui peut
/// écrire au catalogue y répond.
/// </summary>
public static class LoanEndpoints
{
    public static RouteGroupBuilder MapLoanEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/loans")
            .RequireAuthorization(Policies.HouseholdMember)
            .WithTags("Loans");

        group.MapGet("/", ListAsync);
        group.MapPost("/{id:guid}/accept", AcceptAsync).RequireAuthorization(Policies.CatalogueWrite);
        group.MapPost("/{id:guid}/decline", DeclineAsync).RequireAuthorization(Policies.CatalogueWrite);
        group.MapPost("/{id:guid}/return", ReturnAsync).RequireAuthorization(Policies.CatalogueWrite);

        return group;
    }

    /// <summary>
    /// Sans <paramref name="status"/>, ce qui demande une attention : les
    /// demandes en attente et les prêts en cours.
    /// </summary>
    private static async Task<Ok<IReadOnlyList<LoanResponse>>> ListAsync(
        CatalogueDbContext db,
        CancellationToken cancellationToken,
        LoanStatus? status = null,
        Guid? mediaId = null)
    {
        // Aucun filtre de ménage : le schéma du contexte est celui du jeton.
        var query = db.Loans.AsNoTracking();

        query = status is { } wanted
            ? query.Where(l => l.Status == wanted)
            : query.Where(l => l.Status == LoanStatus.Requested || l.Status == LoanStatus.Active);

        if (mediaId is { } wantedMedia)
        {
            query = query.Where(l => l.MediaId == wantedMedia);
        }

        var items = await query
            .Join(db.Medias, l => l.MediaId, m => m.Id, (l, m) => new { Loan = l, Media = m })
            .OrderBy(x => x.Loan.Status == LoanStatus.Requested ? 0 : 1)
            .ThenByDescending(x => x.Loan.ClosedAt ?? x.Loan.LentAt ?? x.Loan.RequestedAt)
            .ThenBy(x => x.Loan.Id)
            .Take(500)
            .Select(x => new LoanResponse(
                x.Loan.Id,
                x.Media.Id,
                x.Media.Type,
                x.Media.Title,
                x.Loan.Status,
                x.Loan.BorrowerName,
                x.Loan.ShareId != null,
                x.Loan.RequestedAt,
                x.Loan.LentAt,
                x.Loan.ClosedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<LoanResponse>>(items);
    }

    /// <summary>
    /// Accepte une demande : le média part, et les autres demandes en attente
    /// sur ce média sont refusées.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> AcceptAsync(
        Guid id,
        CatalogueDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (loan is null)
        {
            return TypedResults.NotFound();
        }

        var media = await db.Medias.FirstAsync(m => m.Id == loan.MediaId, cancellationToken);
        var now = clock.GetUtcNow();

        try
        {
            loan.Accept(media, now);
            await LoanRules.SupersedePendingAsync(db, media.Id, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (LoanException exception)
        {
            return LoanConflict(exception.Message);
        }
        catch (DbUpdateException exception) when (LoanRules.IsSecondActiveLoan(exception))
        {
            return LoanConflict("Ce média vient d'être prêté.");
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeclineAsync(
        Guid id,
        CatalogueDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (loan is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            loan.Decline(clock.GetUtcNow());
        }
        catch (LoanException exception)
        {
            return LoanConflict(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ReturnAsync(
        Guid id,
        CatalogueDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (loan is null)
        {
            return TypedResults.NotFound();
        }

        var media = await db.Medias.FirstAsync(m => m.Id == loan.MediaId, cancellationToken);

        try
        {
            loan.Return(media, clock.GetUtcNow());
        }
        catch (LoanException exception)
        {
            return LoanConflict(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult LoanConflict(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Prêt impossible");
}
