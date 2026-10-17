import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { APP_SETTINGS } from '../app-settings';
import type {
  CreateMediaRequest,
  MediaDetail,
  MediaQuery,
  MediaSummary,
  Paged,
  ScanResult,
  UpdateMediaRequest,
} from './media.models';

/**
 * Client de Catalogue.Api. Un service par Web API, typé depuis les contrats.
 *
 * Le ménage n'apparaît nulle part dans ces appels : l'API le lit dans le
 * jeton. Un client ne doit pas pouvoir désigner le ménage sur lequel il
 * travaille, et ne le peut donc pas depuis ici.
 */
@Injectable({ providedIn: 'root' })
export class CatalogueApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(APP_SETTINGS).apiBaseUrl;

  list(query: MediaQuery = {}): Observable<Paged<MediaSummary>> {
    let params = new HttpParams();

    if (query.type) {
      params = params.set('type', query.type);
    }
    if (query.q && query.q.trim().length > 0) {
      params = params.set('q', query.q.trim());
    }
    if (query.lent !== null && query.lent !== undefined) {
      params = params.set('lent', String(query.lent));
    }
    if (query.page !== undefined) {
      params = params.set('page', String(query.page));
    }
    if (query.size !== undefined) {
      params = params.set('size', String(query.size));
    }

    return this.http.get<Paged<MediaSummary>>(`${this.baseUrl}/medias`, { params });
  }

  get(id: string): Observable<MediaDetail> {
    return this.http.get<MediaDetail>(`${this.baseUrl}/medias/${id}`);
  }

  /** Chemin « scan » : l'ISBN peut être fourni avec ses séparateurs. */
  findByIsbn(isbn: string): Observable<MediaDetail> {
    return this.http.get<MediaDetail>(`${this.baseUrl}/medias/by-isbn/${encodeURIComponent(isbn)}`);
  }

  /**
   * Chemin « scan » complet : doublon dans le ménage, puis notice pré-remplie.
   * L'API ne renvoie jamais d'erreur pour un enrichissement en panne — elle
   * le signale dans `enrichment`, et la saisie reste possible.
   */
  scan(code: string): Observable<ScanResult> {
    return this.http.get<ScanResult>(`${this.baseUrl}/scans/${encodeURIComponent(code)}`);
  }

  create(request: CreateMediaRequest): Observable<MediaDetail> {
    return this.http.post<MediaDetail>(`${this.baseUrl}/medias`, request);
  }

  update(id: string, request: UpdateMediaRequest): Observable<MediaDetail> {
    return this.http.put<MediaDetail>(`${this.baseUrl}/medias/${id}`, request);
  }

  /** Suppression définitive : il n'y a pas de corbeille côté API. */
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/medias/${id}`);
  }

  setLent(id: string, isLent: boolean): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/medias/${id}/lent`, { isLent });
  }
}
