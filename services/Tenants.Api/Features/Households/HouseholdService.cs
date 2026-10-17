using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tenants.Api.Domain;
using Tenants.Api.Features.Members;
using Tenants.Api.Infrastructure.Catalogue;
using Tenants.Api.Infrastructure.Keycloak;
using Tenants.Api.Infrastructure.Persistence;
using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Features.Households;

/// <summary>
/// Cycle de vie des ménages, par l'opérateur : création, archivage,
/// restauration, purge.
/// <para>
/// Séquencée et réparable, comme l'administration des membres : on inscrit
/// d'abord le ménage au registre (<see cref="HouseholdStatus.Provisioning"/>),
/// puis on fait créer son schéma par Catalogue.Api, et seulement alors le
/// ménage devient <see cref="HouseholdStatus.Active"/>. Une création
/// interrompue se reprend en la rejouant à l'identique.
/// </para>
/// <para>
/// L'identifiant est <strong>immuable</strong> : c'est le nom du schéma et
/// la valeur du claim <c>tenant</c>. Aucun chemin ne le renomme.
/// </para>
/// </summary>
public sealed class HouseholdService(
    TenantsDbContext db,
    ICatalogueProvisioning catalogue,
    IIdentityDirectory directory,
    MemberService members,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<HouseholdResponse>> ListAsync(CancellationToken cancellationToken) =>
        await Responses(db.Households.OrderBy(h => h.Id)).ToListAsync(cancellationToken);

    public async Task<HouseholdResponse> CreateAsync(ValidatedCreateHousehold request, CancellationToken cancellationToken)
    {
        var household = await db.Households.SingleOrDefaultAsync(h => h.Id == request.Id.Value, cancellationToken);

        if (household is null)
        {
            household = new Household
            {
                Id = request.Id.Value,
                DisplayName = request.DisplayName,
                CreatedAt = clock.GetUtcNow(),
                Status = HouseholdStatus.Provisioning
            };
            db.Households.Add(household);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Deux créations simultanées du même identifiant : la seconde
                // ne reprend pas silencieusement celle de l'autre.
                throw new HouseholdConflictException("menage-existant", $"Le ménage « {request.Id} » existe déjà.");
            }
        }
        else if (household.Status != HouseholdStatus.Provisioning)
        {
            // Actif, archivé ou en cours de purge : l'identifiant est pris
            // tant que la purge n'est pas allée au bout.
            throw new HouseholdConflictException("menage-existant", $"Le ménage « {request.Id} » existe déjà.");
        }
        else
        {
            // Reprise d'une création interrompue : le nom affiché est celui
            // de la demande rejouée.
            household.DisplayName = request.DisplayName;
            await db.SaveChangesAsync(cancellationToken);
        }

        await catalogue.ProvisionSchemaAsync(request.Id, cancellationToken);

        household.Status = HouseholdStatus.Active;
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(request.Id, cancellationToken);
    }

    /// <summary>
    /// Ajoute un administrateur adulte à un ménage actif : le premier, à la
    /// création, ou un nouveau si le ménage a perdu le sien. Passe par le même
    /// chemin que l'administration des membres — Keycloak, puis le registre.
    /// </summary>
    public async Task<MemberResponse> AddAdminAsync(TenantId household, ValidatedCreateMember request, CancellationToken cancellationToken)
    {
        _ = await RequireAsync(household, cancellationToken);

        // L'opérateur n'est pas membre du ménage : aucun compte du ménage
        // n'est « le sien ».
        return await members.CreateAsync(new Caller(household, Guid.Empty), request with
        {
            Role = MemberRole.Adult,
            IsAdmin = true
        }, cancellationToken);
    }

    /// <summary>
    /// Archive un ménage : désactive tous ses comptes et ferme leurs sessions.
    /// Le catalogue n'est pas touché, et la restauration rend le ménage tel
    /// qu'il était.
    /// <para>
    /// Le ménage passe d'abord <see cref="HouseholdStatus.Archiving"/> — plus
    /// aucun membre ne peut alors y être ajouté ni modifié —, puis
    /// <see cref="HouseholdStatus.Archived"/> une fois tous les comptes
    /// désactivés. Rejouer reprend un archivage interrompu.
    /// </para>
    /// <para>
    /// Limite assumée : un jeton d'accès déjà émis reste valide jusqu'à son
    /// expiration (5 minutes). Catalogue.Api ne consulte pas le registre à
    /// chaque requête, et ne le doit pas ; fermer les sessions empêche
    /// seulement que ce jeton soit renouvelé.
    /// </para>
    /// </summary>
    public async Task<HouseholdResponse> ArchiveAsync(TenantId id, CancellationToken cancellationToken)
    {
        var household = await RequireTrackedAsync(id, cancellationToken);

        switch (household.Status)
        {
            case HouseholdStatus.Archived:
                return await GetAsync(id, cancellationToken);
            case HouseholdStatus.Purging:
                throw new HouseholdConflictException("suppression-en-cours", "Ce ménage est en cours de suppression.");
            case HouseholdStatus.Provisioning:
                // Aucun compte ne peut y être entré : rien à désactiver. Une
                // création abandonnée se supprime directement.
                throw new HouseholdConflictException(
                    "menage-en-creation",
                    "Ce ménage n'a jamais été actif : reprenez sa création, ou supprimez-le directement.");
            case HouseholdStatus.Active:
                household.Status = HouseholdStatus.Archiving;
                household.ArchivedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(cancellationToken);
                break;
        }

        foreach (var account in await directory.ListByTenantAsync(id, cancellationToken))
        {
            if (account.Enabled)
            {
                await directory.SetEnabledAsync(account.Id, false, cancellationToken);
            }

            await directory.SignOutEverywhereAsync(account.Id, cancellationToken);
        }

        household.Status = HouseholdStatus.Archived;
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Restaure un ménage archivé. Ne réactive que les comptes inscrits au
    /// registre que l'administrateur du ménage n'avait pas lui-même
    /// désactivés : l'archivage ne doit rien effacer de ses décisions. Un
    /// compte hors registre reste désactivé, et apparaît dans les écarts.
    /// </summary>
    public async Task<HouseholdResponse> RestoreAsync(TenantId id, CancellationToken cancellationToken)
    {
        var household = await RequireTrackedAsync(id, cancellationToken);

        if (household.Status == HouseholdStatus.Active)
        {
            return await GetAsync(id, cancellationToken);
        }

        if (household.Status is not (HouseholdStatus.Archived or HouseholdStatus.Archiving))
        {
            throw new HouseholdConflictException("menage-non-archive", "Seul un ménage archivé peut être restauré.");
        }

        var activeMembers = await db.Members
            .AsNoTracking()
            .Where(m => m.HouseholdId == id.Value && m.DeactivatedAt == null)
            .Select(m => m.Subject)
            .ToHashSetAsync(cancellationToken);

        foreach (var account in await directory.ListByTenantAsync(id, cancellationToken))
        {
            if (!account.Enabled && activeMembers.Contains(account.Id))
            {
                await directory.SetEnabledAsync(account.Id, true, cancellationToken);
            }
        }

        household.Status = HouseholdStatus.Active;
        household.ArchivedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Supprime définitivement un ménage archivé — ou une création abandonnée,
    /// qui n'a jamais eu de membre : son catalogue (le schéma), ses comptes
    /// Keycloak, puis son registre. Irréversible.
    /// <para>
    /// Le ménage passe d'abord <see cref="HouseholdStatus.Purging"/> : il ne
    /// peut plus être restauré, et une purge interrompue se reprend en la
    /// rejouant. L'identifiant ne redevient libre qu'à la toute fin.
    /// </para>
    /// </summary>
    public async Task PurgeAsync(TenantId id, CancellationToken cancellationToken)
    {
        var household = await RequireTrackedAsync(id, cancellationToken);

        if (household.Status is not (HouseholdStatus.Archived or HouseholdStatus.Purging or HouseholdStatus.Provisioning))
        {
            throw new HouseholdConflictException(
                "menage-non-archive",
                "Un ménage doit être archivé avant d'être supprimé.");
        }

        household.Status = HouseholdStatus.Purging;
        await db.SaveChangesAsync(cancellationToken);

        await catalogue.DropSchemaAsync(id, cancellationToken);

        // Seuls les comptes que Keycloak rattache à ce ménage : une entrée de
        // registre dont le compte est passé ailleurs ne supprime pas ce compte.
        foreach (var account in await directory.ListByTenantAsync(id, cancellationToken))
        {
            await directory.DeleteAsync(account.Id, cancellationToken);
        }

        await db.Members.Where(m => m.HouseholdId == id.Value).ExecuteDeleteAsync(cancellationToken);
        db.Households.Remove(household);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<HouseholdResponse> GetAsync(TenantId household, CancellationToken cancellationToken) =>
        await Responses(db.Households.Where(h => h.Id == household.Value)).SingleAsync(cancellationToken);

    private IQueryable<HouseholdResponse> Responses(IQueryable<Household> households) =>
        households
            .AsNoTracking()
            .Select(h => new HouseholdResponse(
                h.Id,
                h.DisplayName,
                h.CreatedAt,
                h.Status,
                db.Members.Count(m => m.HouseholdId == h.Id),
                h.ArchivedAt));

    private async Task<Household> RequireTrackedAsync(TenantId household, CancellationToken cancellationToken) =>
        await db.Households.SingleOrDefaultAsync(h => h.Id == household.Value, cancellationToken)
            ?? throw new HouseholdNotFoundException();

    private async Task<Household> RequireAsync(TenantId household, CancellationToken cancellationToken) =>
        await db.Households.AsNoTracking().SingleOrDefaultAsync(h => h.Id == household.Value, cancellationToken)
            ?? throw new HouseholdNotFoundException();
}

public sealed record ValidatedCreateHousehold(TenantId Id, string DisplayName);

public sealed class HouseholdNotFoundException() : Exception("Ménage introuvable.");

public sealed class HouseholdConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
