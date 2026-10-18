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
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(Policies.CatalogueWrite);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(Policies.CatalogueWrite);
        group.MapPut("/{id:guid}/lent", SetLentAsync).RequireAuthorization(Policies.CatalogueWrite);

        return group;
    }

    private static async Task<Ok<PagedResponse<MediaSummaryResponse>>> ListAsync(
        CatalogueDbContext db,
        CancellationToken cancellationToken,
        MediaType? type = null,
        string? q = null,
        bool? lent = null,
        MediaSort sort = MediaSort.Recent,
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
            // La série entre dans la recherche : chercher « Blacksad » doit
            // ramener ses albums, dont le titre ne porte pas forcément le nom
            // de la série.
            query = query.Where(m =>
                EF.Functions.ILike(m.Title, pattern) ||
                (m.Creator != null && EF.Functions.ILike(m.Creator, pattern)) ||
                (m.Series != null && EF.Functions.ILike(m.Series, pattern)));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await Sorted(query, sort)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(m => new MediaSummaryResponse(
                m.Id, m.Type, m.Title, m.Creator, m.Year, m.Location, m.IsLent, m.AddedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResponse<MediaSummaryResponse>(items, total, page, size));
    }

    /// <summary>
    /// Ordre de la liste. Chaque tri finit par l'identifiant : sans ordre
    /// total, deux pages successives pourraient se chevaucher ou s'omettre.
    /// Les fiches sans auteur ou sans série viennent après les autres, et
    /// non en tête.
    /// </summary>
    private static IQueryable<Media> Sorted(IQueryable<Media> query, MediaSort sort) => sort switch
    {
        MediaSort.Title => query
            .OrderBy(m => m.Title)
            .ThenBy(m => m.Id),
        MediaSort.Creator => query
            .OrderBy(m => m.Creator == null)
            .ThenBy(m => m.Creator)
            .ThenBy(m => m.Title)
            .ThenBy(m => m.Id),
        MediaSort.Series => query
            .OrderBy(m => m.Series == null)
            .ThenBy(m => m.Series)
            .ThenBy(m => m.Volume == null)
            .ThenBy(m => m.Volume)
            .ThenBy(m => m.Title)
            .ThenBy(m => m.Id),
        _ => query
            .OrderByDescending(m => m.AddedAt)
            .ThenBy(m => m.Id)
    };

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
        var media = await FindByIsbnAsync(db, isbn, cancellationToken);

        return media is null ? TypedResults.NotFound() : TypedResults.Ok(ToDetail(media));
    }

    /// <summary>
    /// Média du ménage portant cet ISBN, sous sa forme 10 ou 13. Partagé par
    /// la recherche par ISBN et par le scan, qui doivent trouver les mêmes
    /// doublons.
    /// </summary>
    internal static async Task<Media?> FindByIsbnAsync(
        CatalogueDbContext db,
        string? isbn,
        CancellationToken cancellationToken)
    {
        var forms = IsbnFormat.Equivalents(isbn);
        if (forms.Count == 0)
        {
            return null;
        }

        return await db.Medias
            .AsNoTracking()
            .Where(m => m.Isbn != null && forms.Contains(m.Isbn))
            .OrderBy(m => m.AddedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Média du ménage portant ce code-barres, sous sa forme canonique
    /// (EAN-13) : un UPC-A et son EAN-13 désignent le même disque.
    /// </summary>
    internal static async Task<Media?> FindByBarcodeAsync(
        CatalogueDbContext db,
        string? barcode,
        CancellationToken cancellationToken)
    {
        var normalized = BarcodeFormat.Normalize(barcode);
        if (normalized is null)
        {
            return null;
        }

        return await db.Medias
            .AsNoTracking()
            .Where(m => m.Barcode == normalized)
            .OrderBy(m => m.AddedAt)
            .FirstOrDefaultAsync(cancellationToken);
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

    private static async Task<Results<Ok<MediaDetailResponse>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id,
        UpdateMediaRequest request,
        CatalogueDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // Aucun filtre de ménage : le schéma du contexte est celui du jeton.
        // Un identifiant d'un autre ménage n'existe pas dans ce schéma, et la
        // correction se solde donc par un 404 — comme une lecture.
        var media = await db.Medias.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (media is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            media.Update(
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

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToDetail(media));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id,
        CatalogueDbContext db,
        CancellationToken cancellationToken)
    {
        var media = await db.Medias.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (media is null)
        {
            return TypedResults.NotFound();
        }

        // Suppression définitive, sans corbeille : un catalogue domestique
        // n'a pas de cycle de rétention, et une fiche effacée se resaisit.
        db.Medias.Remove(media);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
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
