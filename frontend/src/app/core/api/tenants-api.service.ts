import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { APP_SETTINGS } from '../app-settings';
import type {
  CreateHouseholdAdminRequest,
  CreateHouseholdRequest,
  Household,
} from './household.models';
import type { Paged } from './media.models';
import type {
  CreatedInvitation,
  Invitation,
  Share,
  SharedLoan,
  SharedMediaDetail,
  SharedMediaQuery,
  SharedMedia,
} from './sharing.models';
import type {
  CreateMemberRequest,
  Inconsistencies,
  Member,
  UpdateMemberRoleRequest,
} from './member.models';

/**
 * Client de Tenants.Api : administration des membres du ménage, et des
 * ménages eux-mêmes pour l'opérateur.
 *
 * Toute création ou modification de compte passe par ici, jamais par la
 * console Keycloak. Pour les membres, le ménage n'apparaît dans aucun
 * appel : l'API prend celui du jeton de l'administrateur.
 */
@Injectable({ providedIn: 'root' })
export class TenantsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(APP_SETTINGS).tenantsApiBaseUrl;

  members(): Observable<readonly Member[]> {
    return this.http.get<readonly Member[]>(`${this.baseUrl}/members`);
  }

  /** Rejouable : une création interrompue reprend le compte déjà créé. */
  createMember(request: CreateMemberRequest): Observable<Member> {
    return this.http.post<Member>(`${this.baseUrl}/members`, request);
  }

  updateRole(id: string, request: UpdateMemberRoleRequest): Observable<Member> {
    return this.http.put<Member>(`${this.baseUrl}/members/${id}/role`, request);
  }

  /** Le mot de passe est transmis à Keycloak, et devra être changé à la connexion. */
  resetPassword(id: string, temporaryPassword: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/members/${id}/password`, { temporaryPassword });
  }

  deactivate(id: string): Observable<Member> {
    return this.http.post<Member>(`${this.baseUrl}/members/${id}/deactivate`, null);
  }

  reactivate(id: string): Observable<Member> {
    return this.http.post<Member>(`${this.baseUrl}/members/${id}/reactivate`, null);
  }

  /** Écarts entre Keycloak et le registre. L'API ne corrige rien d'elle-même. */
  inconsistencies(): Observable<Inconsistencies> {
    return this.http.get<Inconsistencies>(`${this.baseUrl}/members/inconsistencies`);
  }

  /** Inscrit au registre un compte que Keycloak rattache déjà au ménage. */
  register(id: string): Observable<Member> {
    return this.http.post<Member>(`${this.baseUrl}/members/${id}/register`, null);
  }

  /** Retire une entrée du registre dont le compte a disparu ou quitté le ménage. */
  removeRegistryEntry(subject: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/members/${subject}/registry-entry`);
  }

  // Partage de catalogue entre ménages. L'autre ménage n'est jamais désigné
  // que par un partage : son identifiant n'est pas connu du client.

  /** Le code rendu ne pourra plus être relu : il est à transmettre tout de suite. */
  createInvitation(): Observable<CreatedInvitation> {
    return this.http.post<CreatedInvitation>(`${this.baseUrl}/sharing/invitations`, null);
  }

  invitations(): Observable<readonly Invitation[]> {
    return this.http.get<readonly Invitation[]>(`${this.baseUrl}/sharing/invitations`);
  }

  cancelInvitation(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/sharing/invitations/${id}`);
  }

  acceptInvitation(code: string): Observable<Share> {
    return this.http.post<Share>(`${this.baseUrl}/sharing/invitations/accept`, { code });
  }

  /** Les ménages qui consultent notre catalogue. */
  grantedShares(): Observable<readonly Share[]> {
    return this.http.get<readonly Share[]>(`${this.baseUrl}/sharing/granted`);
  }

  revokeShare(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/sharing/granted/${id}`);
  }

  /** Les catalogues que notre ménage peut consulter. */
  receivedShares(): Observable<readonly Share[]> {
    return this.http.get<readonly Share[]>(`${this.baseUrl}/sharing/received`);
  }

  renounceShare(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/sharing/received/${id}`);
  }

  sharedMedias(shareId: string, query: SharedMediaQuery = {}): Observable<Paged<SharedMedia>> {
    let params = new HttpParams();
    if (query.type) {
      params = params.set('type', query.type);
    }
    if (query.q && query.q.trim().length > 0) {
      params = params.set('q', query.q.trim());
    }
    if (query.sort) {
      params = params.set('sort', query.sort);
    }
    if (query.page !== undefined) {
      params = params.set('page', String(query.page));
    }
    if (query.size !== undefined) {
      params = params.set('size', String(query.size));
    }

    return this.http.get<Paged<SharedMedia>>(`${this.baseUrl}/sharing/received/${shareId}/medias`, { params });
  }

  sharedMedia(shareId: string, mediaId: string): Observable<SharedMediaDetail> {
    return this.http.get<SharedMediaDetail>(`${this.baseUrl}/sharing/received/${shareId}/medias/${mediaId}`);
  }

  /** Rejouable : une demande déjà en attente est rendue, pas dupliquée. */
  requestLoan(shareId: string, mediaId: string): Observable<SharedLoan> {
    return this.http.post<SharedLoan>(
      `${this.baseUrl}/sharing/received/${shareId}/medias/${mediaId}/loan-requests`,
      null,
    );
  }

  sharedLoans(shareId: string): Observable<readonly SharedLoan[]> {
    return this.http.get<readonly SharedLoan[]>(`${this.baseUrl}/sharing/received/${shareId}/loan-requests`);
  }

  cancelLoanRequest(shareId: string, loanId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/sharing/received/${shareId}/loan-requests/${loanId}`);
  }

  // Administration des ménages, réservée à l'opérateur. Ici seulement, le
  // ménage est désigné par l'URL : l'opérateur n'en a pas dans son jeton.

  households(): Observable<readonly Household[]> {
    return this.http.get<readonly Household[]>(`${this.baseUrl}/households`);
  }

  /** Rejouable : une création interrompue reprend le ménage resté en cours. */
  createHousehold(request: CreateHouseholdRequest): Observable<Household> {
    return this.http.post<Household>(`${this.baseUrl}/households`, request);
  }

  /** Désactive les comptes du ménage. Réversible ; rejouable s'il a été interrompu. */
  archiveHousehold(householdId: string): Observable<Household> {
    return this.http.post<Household>(`${this.householdUrl(householdId)}/archive`, null);
  }

  restoreHousehold(householdId: string): Observable<Household> {
    return this.http.post<Household>(`${this.householdUrl(householdId)}/restore`, null);
  }

  /**
   * Supprime définitivement le ménage et son catalogue. L'identifiant doit
   * être ressaisi : l'API refuse sinon.
   */
  purgeHousehold(householdId: string, confirmId: string): Observable<void> {
    return this.http.post<void>(`${this.householdUrl(householdId)}/purge`, { confirmId });
  }

  addHouseholdAdmin(householdId: string, request: CreateHouseholdAdminRequest): Observable<Member> {
    return this.http.post<Member>(`${this.householdUrl(householdId)}/admins`, request);
  }

  private householdUrl(householdId: string): string {
    return `${this.baseUrl}/households/${encodeURIComponent(householdId)}`;
  }
}
