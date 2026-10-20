import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { APP_SETTINGS } from '../app-settings';
import type {
  CatalogueExport,
  CreateMediaRequest,
  FilmDraftResult,
  FilmSearchResult,
  ImportReport,
  Loan,
  LoanStatus,
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
    if (query.sort) {
      params = params.set('sort', query.sort);
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

  /**
   * Vrai si la recherche de films par titre est en service : elle n'existe
   * que si le backend dispose d'une clé OMDb.
   */
  filmSearchAvailable(): Observable<{ readonly enabled: boolean }> {
    return this.http.get<{ readonly enabled: boolean }>(`${this.baseUrl}/films/availability`);
  }

  searchFilms(title: string): Observable<FilmSearchResult> {
    const params = new HttpParams().set('title', title.trim());
    return this.http.get<FilmSearchResult>(`${this.baseUrl}/films`, { params });
  }

  /** Fiche pré-remplie d'un film, d'après son identifiant IMDb. Rien n'est enregistré. */
  film(id: string): Observable<FilmDraftResult> {
    return this.http.get<FilmDraftResult>(`${this.baseUrl}/films/${encodeURIComponent(id)}`);
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

  /** Prêt ou retour saisi par le ménage. Sans nom, l'emprunteur est « Inconnu ». */
  setLent(id: string, isLent: boolean, borrowerName?: string | null): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/medias/${id}/lent`, {
      isLent,
      borrowerName: borrowerName?.trim() || null,
    });
  }

  /** Sans statut : les demandes en attente et les prêts en cours. */
  loans(status?: LoanStatus): Observable<readonly Loan[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.http.get<readonly Loan[]>(`${this.baseUrl}/loans`, { params });
  }

  /** Le média part ; les autres demandes en attente sur ce média sont refusées. */
  acceptLoan(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/loans/${id}/accept`, null);
  }

  declineLoan(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/loans/${id}/decline`, null);
  }

  returnLoan(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/loans/${id}/return`, null);
  }

  /** Tout le catalogue du ménage, en un fichier. */
  exportCatalogue(): Observable<CatalogueExport> {
    return this.http.get<CatalogueExport>(`${this.baseUrl}/medias/export`);
  }

  /**
   * Ajoute les fiches d'un export qui ne sont pas déjà au catalogue. Tout ou
   * rien : une fiche invalide fait refuser le fichier, en 400.
   */
  importCatalogue(file: CatalogueExport): Observable<ImportReport> {
    return this.http.post<ImportReport>(`${this.baseUrl}/medias/import`, file);
  }
}
