using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tenants.Api.Domain;
using Tenants.Api.Infrastructure.Keycloak;
using Tenants.Api.Infrastructure.Persistence;
using Tenants.Api.Infrastructure.Tenancy;

namespace Tenants.Api.Features.Members;

/// <summary>L'administrateur qui agit : son ménage et son compte, tirés du jeton.</summary>
public sealed record Caller(TenantId Tenant, Guid Subject);

/// <summary>
/// Administration des membres d'un ménage — l'inversion d'écriture.
/// <para>
/// Chaque opération écrit <strong>d'abord dans Keycloak, puis dans le
/// registre</strong>. Si la seconde étape échoue, l'opération se rejoue
/// telle quelle sans rien dupliquer : le registre est indexé par le
/// <c>sub</c>, et une création interrompue reprend le compte déjà créé.
/// </para>
/// <para>
/// Un compte d'un autre ménage n'existe pas, pour ce service : il répond
/// « introuvable », jamais « interdit », pour ne rien révéler.
/// </para>
/// </summary>
public sealed class MemberService(
    IIdentityDirectory directory,
    TenantsDbContext db,
    TimeProvider clock,
    ILogger<MemberService> logger)
{
    public async Task<IReadOnlyList<MemberResponse>> ListAsync(Caller caller, CancellationToken cancellationToken)
    {
        var accounts = await directory.ListByTenantAsync(caller.Tenant, cancellationToken);

        var registered = await db.Members
            .AsNoTracking()
            .Where(m => m.HouseholdId == caller.Tenant.Value)
            .Select(m => m.Subject)
            .ToHashSetAsync(cancellationToken);

        List<MemberResponse> members = [];
        foreach (var account in accounts)
        {
            var roles = await directory.GetRealmRolesAsync(account.Id, cancellationToken);
            members.Add(ToResponse(account, roles, registered.Contains(account.Id), caller));
        }

        return members
            .OrderBy(m => m.FirstName ?? m.Username, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Username, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<MemberResponse> CreateAsync(Caller caller, ValidatedCreateMember request, CancellationToken cancellationToken)
    {
        // Avant toute écriture dans Keycloak : un ménage absent du registre
        // laisserait derrière lui un compte qu'on ne pourrait pas inscrire.
        await RequireHouseholdAsync(caller, cancellationToken);

        Guid id;
        var existing = await directory.FindByUsernameAsync(request.Username, cancellationToken);

        if (existing is null)
        {
            id = await directory.CreateAsync(
                new NewAccount(request.Username, request.FirstName, request.LastName, caller.Tenant, request.TemporaryPassword),
                cancellationToken);
        }
        else if (string.Equals(existing.Tenant, caller.Tenant.Value, StringComparison.Ordinal)
                 && !await IsRegisteredAsync(existing.Id, cancellationToken))
        {
            // Reprise d'une création interrompue après l'écriture dans
            // Keycloak : le compte existe déjà dans ce ménage, il manque
            // l'inscription. On réapplique ce que la demande fixe.
            id = existing.Id;
            await directory.SetTemporaryPasswordAsync(id, request.TemporaryPassword, cancellationToken);
        }
        else
        {
            // Pris dans un autre ménage, sans ménage, ou déjà inscrit : même
            // réponse dans tous les cas.
            throw new UsernameTakenException(request.Username);
        }

        await directory.SetManagedRolesAsync(id, Roles.For(request.Role, request.IsAdmin), cancellationToken);

        await RegisterAsync(caller, id, request.Username, cancellationToken);

        return await GetAsync(caller, id, cancellationToken);
    }

    /// <summary>
    /// Incohérences entre Keycloak et le registre, pour le seul ménage de
    /// l'appelant. Rien n'est corrigé ici : chaque réparation est une action
    /// explicite de l'administrateur.
    /// </summary>
    public async Task<InconsistenciesResponse> InconsistenciesAsync(Caller caller, CancellationToken cancellationToken)
    {
        var members = await ListAsync(caller, cancellationToken);

        var entries = await db.Members
            .AsNoTracking()
            .Where(m => m.HouseholdId == caller.Tenant.Value)
            .OrderBy(m => m.Username)
            .ToListAsync(cancellationToken);

        List<OrphanEntryResponse> orphans = [];
        foreach (var entry in entries)
        {
            var account = await directory.FindAsync(entry.Subject, cancellationToken);
            if (account is null)
            {
                orphans.Add(new OrphanEntryResponse(entry.Subject, entry.Username, entry.RegisteredAt, OrphanReason.AccountMissing));
            }
            else if (!string.Equals(account.Tenant, caller.Tenant.Value, StringComparison.Ordinal))
            {
                // Le compte existe, mais n'appartient plus à ce ménage. On ne
                // dit pas où il est : ce n'est pas l'affaire de cet
                // administrateur.
                orphans.Add(new OrphanEntryResponse(entry.Subject, entry.Username, entry.RegisteredAt, OrphanReason.AccountDetached));
            }
        }

        return new InconsistenciesResponse(members.Where(m => !m.Registered).ToList(), orphans);
    }

    /// <summary>
    /// Inscrit au registre un compte que Keycloak rattache déjà à ce ménage.
    /// Rien n'est écrit dans Keycloak. Idempotent.
    /// </summary>
    public async Task<MemberResponse> RegisterAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        var account = await RequireMemberAsync(caller, id, cancellationToken);
        await RequireHouseholdAsync(caller, cancellationToken);
        await RegisterAsync(caller, id, account.Username, cancellationToken);
        return await GetAsync(caller, id, cancellationToken);
    }

    /// <summary>
    /// Retire du registre de ce ménage une entrée dont le compte a disparu ou
    /// n'appartient plus au ménage. Une entrée cohérente est refusée : la
    /// retirer <em>créerait</em> une incohérence. Idempotent — une entrée
    /// absente, ou d'un autre ménage, n'est simplement pas touchée.
    /// </summary>
    public async Task RemoveOrphanEntryAsync(Caller caller, Guid subject, CancellationToken cancellationToken)
    {
        var entry = await db.Members.SingleOrDefaultAsync(
            m => m.Subject == subject && m.HouseholdId == caller.Tenant.Value, cancellationToken);

        if (entry is null)
        {
            return;
        }

        var account = await directory.FindAsync(subject, cancellationToken);
        if (account is not null && string.Equals(account.Tenant, caller.Tenant.Value, StringComparison.Ordinal))
        {
            throw new MemberConflictException(
                "entree-coherente",
                "Ce compte appartient bien au ménage : son inscription au registre n'est pas une incohérence.");
        }

        db.Members.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MemberResponse> UpdateRoleAsync(
        Caller caller, Guid id, MemberRole role, bool isAdmin, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(caller, id, cancellationToken);
        await RequireHouseholdAsync(caller, cancellationToken);

        if (id == caller.Subject && !isAdmin)
        {
            throw new MemberConflictException(
                "soi-meme",
                "Vous ne pouvez pas vous retirer vous-même l'administration du ménage.");
        }

        await directory.SetManagedRolesAsync(id, Roles.For(role, isAdmin), cancellationToken);
        return await GetAsync(caller, id, cancellationToken);
    }

    public async Task ResetPasswordAsync(Caller caller, Guid id, string temporaryPassword, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(caller, id, cancellationToken);
        await RequireHouseholdAsync(caller, cancellationToken);
        await directory.SetTemporaryPasswordAsync(id, temporaryPassword, cancellationToken);
    }

    public async Task<MemberResponse> SetActiveAsync(Caller caller, Guid id, bool active, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(caller, id, cancellationToken);
        await RequireHouseholdAsync(caller, cancellationToken);

        if (id == caller.Subject && !active)
        {
            throw new MemberConflictException("soi-meme", "Vous ne pouvez pas désactiver votre propre compte.");
        }

        await directory.SetEnabledAsync(id, active, cancellationToken);
        if (!active)
        {
            // Un compte désactivé ne se reconnecte plus, mais ses jetons déjà
            // émis restent valides jusqu'à expiration : on ferme ses sessions
            // pour qu'aucun ne soit renouvelé.
            await directory.SignOutEverywhereAsync(id, cancellationToken);
        }

        await RegistryStepAsync(async () =>
        {
            var member = await db.Members.SingleOrDefaultAsync(m => m.Subject == id, cancellationToken);
            if (member is not null)
            {
                member.DeactivatedAt = active ? null : member.DeactivatedAt ?? clock.GetUtcNow();
                await db.SaveChangesAsync(cancellationToken);
            }
        });

        return await GetAsync(caller, id, cancellationToken);
    }

    /// <summary>
    /// Supprime définitivement un membre : son compte Keycloak, puis son
    /// entrée au registre. Seul un compte déjà désactivé se supprime — la
    /// désactivation est le geste réversible, celui-ci ne l'est pas.
    /// <para>
    /// Une suppression interrompue après Keycloak se rejoue : le compte a
    /// disparu, il ne reste que l'entrée de ce ménage à retirer. Le catalogue
    /// n'a rien à nettoyer, il ne connaît aucun compte — un emprunteur y est
    /// un libellé copié.
    /// </para>
    /// </summary>
    public async Task DeleteAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        await RequireHouseholdAsync(caller, cancellationToken);

        var account = await directory.FindAsync(id, cancellationToken);
        if (account is null)
        {
            // Reprise d'une suppression interrompue. Sans entrée ici non plus,
            // le compte n'a jamais été de ce ménage, ou il est déjà supprimé :
            // dans les deux cas, il est introuvable.
            if (!await IsRegisteredHereAsync(caller, id, cancellationToken))
            {
                throw new MemberNotFoundException();
            }
        }
        else
        {
            if (!string.Equals(account.Tenant, caller.Tenant.Value, StringComparison.Ordinal))
            {
                throw new MemberNotFoundException();
            }

            if (id == caller.Subject)
            {
                throw new MemberConflictException("soi-meme", "Vous ne pouvez pas supprimer votre propre compte.");
            }

            if (account.Enabled)
            {
                throw new MemberConflictException(
                    "compte-actif",
                    "Seul un compte désactivé peut être supprimé. Désactivez-le d'abord.");
            }

            await directory.DeleteAsync(id, cancellationToken);
        }

        await RegistryStepAsync(() => db.Members
            .Where(m => m.Subject == id && m.HouseholdId == caller.Tenant.Value)
            .ExecuteDeleteAsync(cancellationToken));
    }

    private async Task<MemberResponse> GetAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        var account = await RequireMemberAsync(caller, id, cancellationToken);
        var roles = await directory.GetRealmRolesAsync(id, cancellationToken);
        return ToResponse(account, roles, await IsRegisteredHereAsync(caller, id, cancellationToken), caller);
    }

    /// <summary>
    /// Le compte, s'il appartient au ménage de l'appelant. L'attribut
    /// <c>tenant</c> de Keycloak fait foi : c'est lui qui produit le claim.
    /// </summary>
    private async Task<DirectoryAccount> RequireMemberAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        var account = await directory.FindAsync(id, cancellationToken);
        return account is not null && string.Equals(account.Tenant, caller.Tenant.Value, StringComparison.Ordinal)
            ? account
            : throw new MemberNotFoundException();
    }

    private async Task RequireHouseholdAsync(Caller caller, CancellationToken cancellationToken)
    {
        // Seul un ménage actif s'administre. En création, il n'a pas encore
        // de catalogue ; archivé, ses comptes ont été désactivés par
        // l'opérateur, et un jeton d'administrateur encore valide ne doit
        // pas pouvoir les réactiver.
        if (!await db.Households.AnyAsync(
                h => h.Id == caller.Tenant.Value && h.Status == HouseholdStatus.Active, cancellationToken))
        {
            throw new MemberConflictException(
                "menage-non-inscrit",
                "Ce ménage n'est pas actif — pas encore créé, ou archivé : ses membres ne peuvent pas être modifiés.");
        }
    }

    /// <summary>Inscrit au registre, dans n'importe quel ménage.</summary>
    private Task<bool> IsRegisteredAsync(Guid id, CancellationToken cancellationToken) =>
        db.Members.AnyAsync(m => m.Subject == id, cancellationToken);

    /// <summary>Inscrit au registre, dans le ménage de l'appelant.</summary>
    private Task<bool> IsRegisteredHereAsync(Caller caller, Guid id, CancellationToken cancellationToken) =>
        db.Members.AnyAsync(m => m.Subject == id && m.HouseholdId == caller.Tenant.Value, cancellationToken);

    /// <summary>
    /// Inscription idempotente au ménage de l'appelant. Un compte déjà inscrit
    /// à un <em>autre</em> ménage est refusé : le registre ne rattache un
    /// compte qu'à un seul ménage, et c'est à l'administrateur de l'autre
    /// ménage de retirer son entrée.
    /// </summary>
    private async Task RegisterAsync(Caller caller, Guid id, string username, CancellationToken cancellationToken)
    {
        await RegistryStepAsync(async () =>
        {
            var entry = await db.Members.SingleOrDefaultAsync(m => m.Subject == id, cancellationToken);
            if (entry is null)
            {
                db.Members.Add(new Member
                {
                    Subject = id,
                    HouseholdId = caller.Tenant.Value,
                    Username = username,
                    RegisteredAt = clock.GetUtcNow()
                });

                await db.SaveChangesAsync(cancellationToken);
            }
        });

        // Relu après coup plutôt que pendant : couvre aussi l'inscription
        // concurrente absorbée par RegistryStepAsync.
        if (!await IsRegisteredHereAsync(caller, id, cancellationToken))
        {
            throw new MemberConflictException(
                "inscrit-ailleurs",
                "Ce compte est encore inscrit au registre d'un autre ménage. Son entrée doit d'abord y être retirée.");
        }
    }

    /// <summary>
    /// Seconde étape d'une opération : Keycloak est déjà à jour. Un échec ici
    /// ne défait rien — il est signalé comme rejouable.
    /// </summary>
    private async Task RegistryStepAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Une requête concurrente a inscrit le même sub : le résultat
            // recherché est atteint.
        }
        catch (Exception exception) when (exception is DbUpdateException or NpgsqlException or InvalidOperationException)
        {
            logger.LogError(exception, "Keycloak à jour, registre en échec : opération à rejouer.");
            throw new RegistryUnavailableException();
        }
    }

    private static MemberResponse ToResponse(DirectoryAccount account, IReadOnlySet<string> roles, bool registered, Caller caller) => new(
        account.Id,
        account.Username,
        account.FirstName,
        account.LastName,
        Roles.RoleOf(roles),
        roles.Contains(Roles.HouseholdAdmin),
        account.Enabled,
        registered,
        account.Id == caller.Subject);
}

public sealed class MemberNotFoundException() : Exception("Membre introuvable dans ce ménage.");

/// <summary>Refus métier, avec un code stable que l'interface peut reconnaître.</summary>
public sealed class MemberConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class RegistryUnavailableException()
    : Exception("Keycloak est à jour, mais pas le registre des ménages : relancez l'opération.");
