import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { APP_SETTINGS } from '../app-settings';
import type {
  CreateHouseholdAdminRequest,
  CreateHouseholdRequest,
  Household,
} from './household.models';
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
