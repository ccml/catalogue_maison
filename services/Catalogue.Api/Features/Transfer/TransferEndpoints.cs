using Catalogue.Api.Domain;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Catalogue.Api.Features.Transfer;

/// <summary>
/// Export et import du catalogue du ménage, en un fichier JSON. Réservés à
/// qui peut écrire au catalogue : un export est une copie de tout le
/// catalogue, un import en ajoute en masse.
/// </summary>
public static class TransferEndpoints
{
    /// <summary>
    /// Au-delà, le fichier est refusé d'emblée : c'est dix fois une grande
    /// bibliothèque domestique, et un import se fait en une seule transaction.
    /// </summary>
    public const int MaxMedias = 20_000;

    /// <summary>Messages d'erreur rendus au plus : au-delà, la liste ne se lit plus.</summary>
    private const int MaxReportedErrors = 50;

    public static RouteGroupBuilder MapTransferEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/medias")
            .RequireAuthorization(Policies.HouseholdMember)
            .RequireAuthorization(Policies.CatalogueWrite)
            .WithTags("Transfer");

        group.MapGet("/export", ExportAsync);
        group.MapPost("/import", ImportAsync);

        return group;
    }

    private static async Task<Ok<CatalogueExport>> ExportAsync(
        CatalogueDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // Aucun filtre de ménage : le schéma du contexte est celui du jeton.
        var medias = await db.Medias
            .AsNoTracking()
            .OrderBy(m => m.AddedAt)
            .ThenBy(m => m.Id)
            .Select(m => new ExportedMedia(
                m.Type, m.Title, m.Creator, m.Year, m.Location, m.IsLent, m.AddedAt, m.Specifics))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new CatalogueExport(
            CatalogueExport.FormatName,
            CatalogueExport.CurrentVersion,
            clock.GetUtcNow(),
            medias));
    }

    /// <summary>
    /// Ajoute au catalogue les fiches du fichier qui n'y sont pas déjà.
    /// <strong>Tout ou rien</strong> : une seule fiche invalide fait refuser
    /// le fichier entier, sans rien écrire — un import à moitié fait serait
    /// plus difficile à réparer qu'un refus. Rien n'est jamais supprimé ni
    /// écrasé : les doublons sont sautés, et rapportés.
    /// </summary>
    private static async Task<Results<Ok<ImportReport>, ValidationProblem>> ImportAsync(
        CatalogueExport file,
        CatalogueDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (file.Format != CatalogueExport.FormatName)
        {
            return Refused("Ce fichier n'est pas un export de CatalogueMaison.");
        }

        if (file.Version != CatalogueExport.CurrentVersion)
        {
            return Refused($"Version de fichier {file.Version} non prise en charge.");
        }

        if (file.Medias is null)
        {
            return Refused("Le fichier ne contient aucune liste de médias.");
        }

        if (file.Medias.Count > MaxMedias)
        {
            return Refused($"Le fichier dépasse {MaxMedias} médias.");
        }

        var now = clock.GetUtcNow();
        var candidates = new List<(Media Media, bool IsLent)>(file.Medias.Count);
        List<string> errors = [];

        for (var i = 0; i < file.Medias.Count; i++)
        {
            var entry = file.Medias[i];
            try
            {
                candidates.Add((Media.Import(
                    entry.Type,
                    entry.Title,
                    entry.Creator,
                    entry.Year,
                    entry.Location,
                    entry.Specifics,
                    entry.AddedAt,
                    now), entry.IsLent));
            }
            catch (MediaValidationException exception)
            {
                var label = string.IsNullOrWhiteSpace(entry.Title) ? "" : $" ({entry.Title.Trim()})";
                errors.AddRange(exception.Errors.Select(error => $"Fiche {i + 1}{label} : {error}"));
            }
        }

        if (errors.Count > 0)
        {
            if (errors.Count > MaxReportedErrors)
            {
                var hidden = errors.Count - MaxReportedErrors;
                errors = [.. errors.Take(MaxReportedErrors), $"… et {hidden} autres erreurs."];
            }

            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["medias"] = [.. errors]
            });
        }

        var detector = new DuplicateDetector();
        var existing = await db.Medias
            .AsNoTracking()
            .Select(m => new { m.Type, m.Title, m.Creator, m.Isbn, m.Barcode })
            .ToListAsync(cancellationToken);

        foreach (var media in existing)
        {
            detector.AddExisting(media.Type, media.Title, media.Creator, media.Isbn, media.Barcode);
        }

        List<SkippedMedia> skipped = [];
        for (var i = 0; i < candidates.Count; i++)
        {
            var (media, isLent) = candidates[i];
            if (detector.Check(media.Type, media.Title, media.Creator, media.Specifics) is { } reason)
            {
                skipped.Add(new SkippedMedia(i + 1, media.Type, media.Title, reason));
                continue;
            }

            db.Medias.Add(media);

            // Le fichier ne dit que « prêté » : l'emprunteur n'en fait pas
            // partie, il est inconnu.
            if (isLent)
            {
                db.Loans.Add(Loan.Lend(media, Loan.UnknownBorrower, now));
            }
        }

        // Un seul SaveChanges : une seule transaction.
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ImportReport(candidates.Count - skipped.Count, skipped));
    }

    private static ValidationProblem Refused(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
}
