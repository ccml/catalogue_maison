using Catalogue.Api.Domain;
using Catalogue.Api.Features.Loans;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Persistence;
using Catalogue.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Catalogue.Api.Features.Sharing;

/// <summary>
/// Le catalogue d'un ménage, lu pour un ménage partenaire, et les demandes de
/// prêt de ce partenaire. Appelé par Tenants.Api seul, en client credentials.
/// <para>
/// Ce service ne décide pas qui partage avec qui : Tenants.Api tient les
/// partages, vérifie que le ménage qui demande est bien celui à qui le
/// catalogue est partagé, et désigne ici le ménage prêteur et le partage. Ni
/// l'un ni l'autre ne vient jamais d'un client : un jeton de personne n'a pas
/// le rôle de partage, et un jeton qui porte un claim <c>tenant</c> est refusé.
/// </para>
/// <para>
/// Tout ce qui est rendu est restreint : ni emplacement, ni emprunteur, ni
/// date d'ajout, et seulement les demandes faites par ce partage.
/// </para>
/// </summary>
public static class SharingEndpoints
{
    public static RouteGroupBuilder MapSharingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/internal/households/{household}/shares/{shareId:guid}")
            .RequireAuthorization(Policies.Sharing)
            .WithMetadata(ServiceEndpoint.Sharing)
            .WithTags("Sharing");

        group.MapGet("/medias", ListAsync);
        group.MapGet("/medias/{id:guid}", GetAsync);
        group.MapPost("/medias/{id:guid}/loan-requests", RequestLoanAsync);
        group.MapGet("/loan-requests", ListLoansAsync);
        group.MapDelete("/loan-requests/{loanId:guid}", CancelAsync);

        return group;
    }

    private static async Task<Results<Ok<PagedResponse<SharedMediaResponse>>, NotFound, ProblemHttpResult>> ListAsync(
        string household,
        Guid shareId,
        HouseholdContextFactory contexts,
        CancellationToken cancellationToken,
        MediaType? type = null,
        string? q = null,
        MediaSort sort = MediaSort.Recent,
        int page = 1,
        int size = 50)
    {
        var (opened, refusal) = await OpenAsync(household, contexts, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        await using var db = opened!;

        page = Math.Max(page, 1);
        size = Math.Clamp(size, 1, 200);

        var query = MediaEndpoints.Filtered(db.Medias.AsNoTracking(), type, q);
        var total = await query.CountAsync(cancellationToken);

        var rows = await MediaEndpoints.Sorted(query, sort)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(m => new
            {
                m.Id, m.Type, m.Title, m.Creator, m.Year, m.Series, m.Volume, m.IsLent,
                LentToYou = db.Loans.Any(l => l.MediaId == m.Id && l.ShareId == shareId && l.Status == LoanStatus.Active),
                RequestedByYou = db.Loans.Any(l => l.MediaId == m.Id && l.ShareId == shareId && l.Status == LoanStatus.Requested)
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new SharedMediaResponse(
                r.Id, r.Type, r.Title, r.Creator, r.Year, r.Series, r.Volume,
                Availability(r.IsLent, r.LentToYou, r.RequestedByYou)))
            .ToList();

        return TypedResults.Ok(new PagedResponse<SharedMediaResponse>(items, total, page, size));
    }

    private static async Task<Results<Ok<SharedMediaDetailResponse>, NotFound, ProblemHttpResult>> GetAsync(
        string household,
        Guid shareId,
        Guid id,
        HouseholdContextFactory contexts,
        CancellationToken cancellationToken)
    {
        var (opened, refusal) = await OpenAsync(household, contexts, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        await using var db = opened!;

        var media = await db.Medias.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (media is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new SharedMediaDetailResponse(
            media.Id, media.Type, media.Title, media.Creator, media.Year,
            await AvailabilityAsync(db, media, shareId, cancellationToken),
            media.Specifics));
    }

    /// <summary>
    /// Dépose une demande de prêt. Rejouée alors qu'une demande de ce partage
    /// est déjà en attente sur ce média, elle rend cette demande plutôt que
    /// d'en créer une seconde.
    /// </summary>
    private static async Task<Results<Created<SharedLoanResponse>, Ok<SharedLoanResponse>, NotFound, ProblemHttpResult>> RequestLoanAsync(
        string household,
        Guid shareId,
        Guid id,
        CreateLoanRequestRequest request,
        HouseholdContextFactory contexts,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (opened, refusal) = await OpenAsync(household, contexts, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        await using var db = opened!;

        var media = await db.Medias.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (media is null)
        {
            return TypedResults.NotFound();
        }

        var pending = await db.Loans.AsNoTracking().FirstOrDefaultAsync(
            l => l.MediaId == id && l.ShareId == shareId && l.Status == LoanStatus.Requested,
            cancellationToken);

        if (pending is not null)
        {
            return TypedResults.Ok(ToResponse(pending, media));
        }

        Loan loan;
        try
        {
            loan = Loan.Request(media, shareId, request.BorrowerName, clock.GetUtcNow());
        }
        catch (LoanException exception)
        {
            return Conflict(exception.Message);
        }

        db.Loans.Add(loan);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"loan-requests/{loan.Id}", ToResponse(loan, media));
    }

    /// <summary>
    /// Les demandes et prêts de ce partage : en attente et en cours d'abord,
    /// puis les plus récents.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<SharedLoanResponse>>, NotFound, ProblemHttpResult>> ListLoansAsync(
        string household,
        Guid shareId,
        HouseholdContextFactory contexts,
        CancellationToken cancellationToken)
    {
        var (opened, refusal) = await OpenAsync(household, contexts, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        await using var db = opened!;

        var items = await db.Loans
            .AsNoTracking()
            .Where(l => l.ShareId == shareId)
            .Join(db.Medias, l => l.MediaId, m => m.Id, (l, m) => new { Loan = l, Media = m })
            .OrderBy(x => x.Loan.Status == LoanStatus.Requested ? 0 : x.Loan.Status == LoanStatus.Active ? 1 : 2)
            .ThenByDescending(x => x.Loan.ClosedAt ?? x.Loan.LentAt ?? x.Loan.RequestedAt)
            .ThenBy(x => x.Loan.Id)
            .Take(100)
            .Select(x => new SharedLoanResponse(
                x.Loan.Id, x.Media.Id, x.Media.Type, x.Media.Title, x.Loan.Status,
                x.Loan.RequestedAt, x.Loan.LentAt, x.Loan.ClosedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<SharedLoanResponse>>(items);
    }

    /// <summary>Retrait d'une demande par le ménage qui l'avait faite, et par lui seul.</summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> CancelAsync(
        string household,
        Guid shareId,
        Guid loanId,
        HouseholdContextFactory contexts,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (opened, refusal) = await OpenAsync(household, contexts, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        await using var db = opened!;

        // Une demande d'un autre partage n'existe pas, pour celui-ci.
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.Id == loanId && l.ShareId == shareId, cancellationToken);
        if (loan is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            loan.Cancel(clock.GetUtcNow());
        }
        catch (LoanException exception)
        {
            return Conflict(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Ouvre le schéma du ménage prêteur, ou dit pourquoi il ne s'ouvre pas :
    /// identifiant invalide, ou schéma absent ou en retard — refusé comme pour
    /// un membre du ménage.
    /// </summary>
    private static async Task<(CatalogueDbContext? Db, ProblemHttpResult? Refusal)> OpenAsync(
        string household,
        HouseholdContextFactory contexts,
        CancellationToken cancellationToken)
    {
        if (!TenantId.TryCreate(household, out var tenant) || tenant is null || tenant.Value != household)
        {
            return (null, TypedResults.Problem(statusCode: StatusCodes.Status404NotFound));
        }

        var db = await contexts.OpenUpToDateAsync(tenant, cancellationToken);

        return db is null
            ? (null, TypedResults.Problem(
                "Le catalogue de ce ménage n'est pas à la version attendue.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Catalogue indisponible",
                type: "https://cataloguemaison/errors/schema-en-retard"))
            : (db, null);
    }

    private static async Task<SharedAvailability> AvailabilityAsync(
        CatalogueDbContext db,
        Media media,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        var mine = await db.Loans
            .AsNoTracking()
            .Where(l => l.MediaId == media.Id && l.ShareId == shareId
                        && (l.Status == LoanStatus.Active || l.Status == LoanStatus.Requested))
            .Select(l => l.Status)
            .ToListAsync(cancellationToken);

        return Availability(media.IsLent, mine.Contains(LoanStatus.Active), mine.Contains(LoanStatus.Requested));
    }

    private static SharedAvailability Availability(bool isLent, bool lentToYou, bool requestedByYou) =>
        lentToYou ? SharedAvailability.LentToYou
        : isLent ? SharedAvailability.Lent
        : requestedByYou ? SharedAvailability.RequestedByYou
        : SharedAvailability.Available;

    private static SharedLoanResponse ToResponse(Loan loan, Media media) => new(
        loan.Id, media.Id, media.Type, media.Title, loan.Status, loan.RequestedAt, loan.LentAt, loan.ClosedAt);

    private static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Demande impossible");
}
