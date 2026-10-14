using Catalogue.Api.Domain;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Catalogue.Api.Features.Medias;

public static class MediaEndpoints
{
    public static RouteGroupBuilder MapMediaEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/medias")
            .RequireAuthorization(Policies.HouseholdMember)
            .WithTags("Medias");

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/by-isbn/{isbn}", GetByIsbnAsync);
        group.MapPost("/", CreateAsync).RequireAuthorization(Policies.CatalogueWrite);
        group.MapPut("/{id:guid}/lent", SetLentAsync).RequireAuthorization(Policies.CatalogueWrite);

        return group;
    }

    private static async Task<Ok<PagedResponse<MediaSummaryResponse>>> ListAsync(
        CatalogueDbContext db,
        CancellationToken cancellationToken,
        MediaType? type = null,
        string? q = null,
        bool? lent = null,
        int page = 1,
        int size = 50)
    {
        page = Math.Max(page, 1);
        size = Math.Clamp(size, 1, 200);

        var query = db.Medias.AsNoTracking();

        if (type is { } wantedType)
        {
            query = query.Where(m => m.Type == wantedType);
        }

        if (lent is { } wantedLent)
        {
            query = query.Where(m => m.IsLent == wantedLent);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(m =>
                EF.Functions.ILike(m.Title, pattern) ||
                (m.Creator != null && EF.Functions.ILike(m.Creator, pattern)));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.AddedAt)
            .ThenBy(m => m.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(m => new MediaSummaryResponse(
                m.Id, m.Type, m.Title, m.Creator, m.Year, m.Location, m.IsLent, m.AddedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResponse<MediaSummaryResponse>(items, total, page, size));
    }

    private static async Task<Results<Ok<MediaDetailResponse>, NotFound>> GetAsync(
        Guid id,
        CatalogueDbContext db,
        CancellationToken cancellationToken)
    {
        // Aucun filtre de ménage ici : le schéma du contexte est celui du
        // ménage du jeton. Un identifiant appartenant à un autre ménage
        // n'existe tout simplement pas dans ce schéma.
        var media = await db.Medias.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        return media is null ? TypedResults.NotFound() : TypedResults.Ok(ToDetail(media));
    }

    private static async Task<Results<Ok<MediaDetailResponse>, NotFound>> GetByIsbnAsync(
        string isbn,
        CatalogueDbContext db,
        CancellationToken cancellationToken)
    {
        var normalized = IsbnFormat.Normalize(isbn);
        if (normalized is null)
        {
            return TypedResults.NotFound();
        }

        var media = await db.Medias
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Isbn == normalized, cancellationToken);

        return media is null ? TypedResults.NotFound() : TypedResults.Ok(ToDetail(media));
    }

    private static async Task<Results<Created<MediaDetailResponse>, ValidationProblem>> CreateAsync(
        CreateMediaRequest request,
        CatalogueDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        Media media;
        try
        {
            media = Media.Create(
                request.Type,
                request.Title,
                request.Creator,
                request.Year,
                request.Location,
                request.Specifics,
                clock.GetUtcNow());
        }
        catch (MediaValidationException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["media"] = [.. exception.Errors]
            });
        }

        db.Medias.Add(media);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/medias/{media.Id}", ToDetail(media));
    }

    private static async Task<Results<NoContent, NotFound>> SetLentAsync(
        Guid id,
        SetLentRequest request,
        CatalogueDbContext db,
        CancellationToken cancellationToken)
    {
        var media = await db.Medias.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (media is null)
        {
            return TypedResults.NotFound();
        }

        media.SetLent(request.IsLent);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static MediaDetailResponse ToDetail(Media media) => new(
        media.Id,
        media.Type,
        media.Title,
        media.Creator,
        media.Year,
        media.Location,
        media.IsLent,
        media.AddedAt,
        media.Specifics);
}
