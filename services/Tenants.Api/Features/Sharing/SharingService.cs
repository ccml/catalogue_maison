using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tenants.Api.Infrastructure.Persistence;
using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Features.Sharing;

/// <summary>
/// Les partages de catalogue entre ménages : un ménage invite, un autre
/// accepte, et peut dès lors consulter le catalogue du premier.
/// <para>
/// Le ménage de l'appelant vient toujours du claim <c>tenant</c> ; l'autre
/// ménage n'est jamais désigné par le client. Celui qui invite ne le connaît
/// pas : il transmet un code. Celui qui consulte ne désigne qu'un partage,
/// dont on vérifie qu'il lui est accordé.
/// </para>
/// </summary>
public sealed class SharingService(TenantsDbContext db, TimeProvider clock)
{
    /// <summary>Durée de validité d'une invitation.</summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    /// <summary>Invitations en attente au plus par ménage : au-delà, il faut en annuler.</summary>
    public const int MaxPendingInvitations = 10;

    public async Task<CreatedInvitationResponse> InviteAsync(TenantId owner, CancellationToken cancellationToken)
    {
        await RequireActiveAsync(owner, cancellationToken);

        var now = clock.GetUtcNow();
        await ForgetExpiredAsync(owner, now, cancellationToken);

        if (await db.ShareInvitations.CountAsync(i => i.OwnerHouseholdId == owner.Value, cancellationToken)
            >= MaxPendingInvitations)
        {
            throw new SharingConflictException(
                "trop-d-invitations",
                $"Votre ménage a déjà {MaxPendingInvitations} invitations en attente. Annulez celles qui ne serviront pas.");
        }

        var code = InvitationCode.Generate();
        var invitation = new ShareInvitation
        {
            Id = Guid.CreateVersion7(),
            OwnerHouseholdId = owner.Value,
            CodeHash = InvitationCode.Hash(InvitationCode.Normalize(code)!),
            CreatedAt = now,
            ExpiresAt = now + InvitationLifetime
        };

        db.ShareInvitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatedInvitationResponse(invitation.Id, code, invitation.ExpiresAt);
    }

    public async Task<IReadOnlyList<InvitationResponse>> ListInvitationsAsync(TenantId owner, CancellationToken cancellationToken)
    {
        await ForgetExpiredAsync(owner, clock.GetUtcNow(), cancellationToken);

        return await db.ShareInvitations
            .AsNoTracking()
            .Where(i => i.OwnerHouseholdId == owner.Value)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationResponse(i.Id, i.CreatedAt, i.ExpiresAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Annule une invitation du ménage. Idempotent.</summary>
    public async Task CancelInvitationAsync(TenantId owner, Guid id, CancellationToken cancellationToken) =>
        await db.ShareInvitations
            .Where(i => i.Id == id && i.OwnerHouseholdId == owner.Value)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Accepte une invitation : le ménage de l'appelant peut désormais
    /// consulter le catalogue du ménage qui a invité. L'invitation est
    /// consommée. Si ce partage existait déjà, il est rendu tel quel.
    /// </summary>
    /// <exception cref="InvitationRejectedException">
    /// Code inconnu, expiré, déjà utilisé, ou émis par le ménage de
    /// l'appelant. Les trois premiers cas ne se distinguent pas : rien ne
    /// doit renseigner sur l'existence d'un code.
    /// </exception>
    public async Task<ShareResponse> AcceptAsync(TenantId viewer, string? code, CancellationToken cancellationToken)
    {
        await RequireActiveAsync(viewer, cancellationToken);

        var normalized = InvitationCode.Normalize(code) ?? throw InvitationRejectedException.Unknown();
        var hash = InvitationCode.Hash(normalized);
        var now = clock.GetUtcNow();

        var invitation = await db.ShareInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.CodeHash == hash && i.ExpiresAt > now, cancellationToken)
            ?? throw InvitationRejectedException.Unknown();

        if (invitation.OwnerHouseholdId == viewer.Value)
        {
            throw new InvitationRejectedException(
                "Ce code a été créé par votre propre ménage : il est destiné au ménage avec qui vous partagez.");
        }

        var owner = await db.Households
            .AsNoTracking()
            .SingleOrDefaultAsync(h => h.Id == invitation.OwnerHouseholdId && h.Status == HouseholdStatus.Active, cancellationToken)
            ?? throw InvitationRejectedException.Unknown();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Usage unique, y compris face à deux acceptations simultanées : seule
        // celle qui supprime l'invitation continue.
        if (await db.ShareInvitations.Where(i => i.Id == invitation.Id).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            throw InvitationRejectedException.Unknown();
        }

        var existing = await db.Shares.AsNoTracking().SingleOrDefaultAsync(
            s => s.OwnerHouseholdId == owner.Id && s.ViewerHouseholdId == viewer.Value, cancellationToken);

        var share = existing ?? new Share
        {
            Id = Guid.CreateVersion7(),
            OwnerHouseholdId = owner.Id,
            ViewerHouseholdId = viewer.Value,
            CreatedAt = now
        };

        if (existing is null)
        {
            db.Shares.Add(share);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Deux invitations du même ménage acceptées au même instant :
                // l'autre a créé le partage, qui est celui-ci.
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                var winner = await db.Shares.AsNoTracking().SingleAsync(
                    s => s.OwnerHouseholdId == owner.Id && s.ViewerHouseholdId == viewer.Value, cancellationToken);
                return new ShareResponse(winner.Id, owner.DisplayName, winner.CreatedAt);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return new ShareResponse(share.Id, owner.DisplayName, share.CreatedAt);
    }

    /// <summary>Les ménages qui consultent le catalogue de celui-ci.</summary>
    public async Task<IReadOnlyList<ShareResponse>> ListGrantedAsync(TenantId owner, CancellationToken cancellationToken) =>
        await db.Shares
            .AsNoTracking()
            .Where(s => s.OwnerHouseholdId == owner.Value)
            .Join(db.Households, s => s.ViewerHouseholdId, h => h.Id, (s, h) => new { Share = s, Viewer = h })
            .OrderBy(x => x.Viewer.DisplayName)
            .Select(x => new ShareResponse(x.Share.Id, x.Viewer.DisplayName, x.Share.CreatedAt))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Les catalogues que ce ménage peut consulter. Un ménage archivé n'y
    /// figure plus ; restauré, il y revient.
    /// </summary>
    public async Task<IReadOnlyList<ShareResponse>> ListReceivedAsync(TenantId viewer, CancellationToken cancellationToken) =>
        await db.Shares
            .AsNoTracking()
            .Where(s => s.ViewerHouseholdId == viewer.Value)
            .Join(db.Households, s => s.OwnerHouseholdId, h => h.Id, (s, h) => new { Share = s, Owner = h })
            .Where(x => x.Owner.Status == HouseholdStatus.Active)
            .OrderBy(x => x.Owner.DisplayName)
            .Select(x => new ShareResponse(x.Share.Id, x.Owner.DisplayName, x.Share.CreatedAt))
            .ToListAsync(cancellationToken);

    /// <summary>Le ménage prêteur retire le partage. Idempotent.</summary>
    public async Task RevokeAsync(TenantId owner, Guid shareId, CancellationToken cancellationToken) =>
        await db.Shares
            .Where(s => s.Id == shareId && s.OwnerHouseholdId == owner.Value)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>Le ménage qui consulte renonce au partage. Idempotent.</summary>
    public async Task RenounceAsync(TenantId viewer, Guid shareId, CancellationToken cancellationToken) =>
        await db.Shares
            .Where(s => s.Id == shareId && s.ViewerHouseholdId == viewer.Value)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Le partage <paramref name="shareId"/>, s'il est accordé au ménage de
    /// l'appelant et que les deux ménages sont actifs. Ce qui est rendu sert
    /// à appeler Catalogue.Api ; il ne sort jamais vers le client.
    /// </summary>
    /// <exception cref="ShareNotFoundException">
    /// Dans tous les autres cas, sans distinction : un partage d'un autre
    /// ménage n'existe pas, pour celui-ci.
    /// </exception>
    public async Task<ReceivedShare> ResolveReceivedAsync(TenantId viewer, Guid shareId, CancellationToken cancellationToken)
    {
        var found = await db.Shares
            .AsNoTracking()
            .Where(s => s.Id == shareId && s.ViewerHouseholdId == viewer.Value)
            .Join(db.Households, s => s.OwnerHouseholdId, h => h.Id, (s, owner) => new { Share = s, Owner = owner })
            .Join(db.Households, x => x.Share.ViewerHouseholdId, h => h.Id, (x, viewerHousehold) => new
            {
                x.Share.Id,
                OwnerId = x.Owner.Id,
                OwnerStatus = x.Owner.Status,
                ViewerName = viewerHousehold.DisplayName,
                ViewerStatus = viewerHousehold.Status
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (found is null
            || found.OwnerStatus != HouseholdStatus.Active
            || found.ViewerStatus != HouseholdStatus.Active)
        {
            throw new ShareNotFoundException();
        }

        return new ReceivedShare(found.Id, TenantId.Create(found.OwnerId), found.ViewerName);
    }

    /// <summary>
    /// Efface tout ce qui touche aux partages d'un ménage, dans les deux sens.
    /// Étape de la purge, avant la suppression du ménage du registre.
    /// </summary>
    public async Task ForgetHouseholdAsync(TenantId household, CancellationToken cancellationToken)
    {
        await db.ShareInvitations.Where(i => i.OwnerHouseholdId == household.Value).ExecuteDeleteAsync(cancellationToken);
        await db.Shares
            .Where(s => s.OwnerHouseholdId == household.Value || s.ViewerHouseholdId == household.Value)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task ForgetExpiredAsync(TenantId owner, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.ShareInvitations
            .Where(i => i.OwnerHouseholdId == owner.Value && i.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);

    private async Task RequireActiveAsync(TenantId household, CancellationToken cancellationToken)
    {
        if (!await db.Households.AnyAsync(h => h.Id == household.Value && h.Status == HouseholdStatus.Active, cancellationToken))
        {
            throw new SharingConflictException("menage-inactif", "Votre ménage n'est pas actif.");
        }
    }
}

/// <summary>
/// Un partage reçu, résolu côté serveur. <see cref="Owner"/> désigne un
/// schéma : il ne sert qu'à appeler Catalogue.Api.
/// </summary>
public sealed record ReceivedShare(Guid Id, TenantId Owner, string ViewerName);

/// <summary>Partage inconnu, ou qui n'est pas accordé au ménage de l'appelant.</summary>
public sealed class ShareNotFoundException() : Exception("Ce catalogue n'est pas partagé avec votre ménage.");

public sealed class InvitationRejectedException(string message) : Exception(message)
{
    public static InvitationRejectedException Unknown() =>
        new("Ce code d'invitation n'est pas valable : il est erroné, expiré ou déjà utilisé.");
}

public sealed class SharingConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
