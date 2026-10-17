import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { APP_SETTINGS } from '../app-settings';
import type {
  CreateMemberRequest,
  Inconsistencies,
  Member,
  UpdateMemberRoleRequest,
} from './member.models';

/**
 * Client de Tenants.Api : administration des membres du ménage.
 *
 * Toute création ou modification de compte passe par ici, jamais par la
 * console Keycloak. Le ménage n'apparaît dans aucun appel : l'API prend
 * celui du jeton de l'administrateur.
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
}
