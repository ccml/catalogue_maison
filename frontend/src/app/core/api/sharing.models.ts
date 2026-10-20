import { HttpErrorResponse } from '@angular/common/http';
import { describeHttpError, type ApiProblem } from './http-error';
import type { LoanStatus, MediaSort, MediaSpecifics, MediaType } from './media.models';

/**
 * Un partage, vu de l'un ou l'autre côté. `householdName` est le nom affiché
 * de l'autre ménage : l'API ne donne jamais son identifiant.
 */
export interface Share {
  readonly id: string;
  readonly householdName: string;
  readonly since: string;
}

/** Invitation tout juste créée : son code ne sera plus jamais relisible. */
export interface CreatedInvitation {
  readonly id: string;
  readonly code: string;
  readonly expiresAt: string;
}

export interface Invitation {
  readonly id: string;
  readonly createdAt: string;
  readonly expiresAt: string;
}

/**
 * Ce qu'un ménage partenaire sait de la disponibilité d'un média : prêté ou
 * non, et s'il l'a lui-même demandé ou emprunté — jamais à qui il est prêté.
 */
export type SharedAvailability = 'Available' | 'Lent' | 'LentToYou' | 'RequestedByYou';

export const sharedAvailabilityLabels: Readonly<Record<SharedAvailability, string>> = {
  Available: 'Disponible',
  Lent: 'Déjà prêté',
  LentToYou: 'Chez vous',
  RequestedByYou: 'Demandé',
};

/** Fiche vue d'un catalogue partagé : ni emplacement, ni date d'ajout. */
export interface SharedMedia {
  readonly id: string;
  readonly type: MediaType;
  readonly title: string;
  readonly creator?: string | null;
  readonly year?: number | null;
  readonly series?: string | null;
  readonly volume?: number | null;
  readonly availability: SharedAvailability;
}

export interface SharedMediaDetail {
  readonly id: string;
  readonly type: MediaType;
  readonly title: string;
  readonly creator?: string | null;
  readonly year?: number | null;
  readonly availability: SharedAvailability;
  readonly specifics?: MediaSpecifics | null;
}

/** Une demande ou un prêt, vu par le ménage qui l'a fait. */
export interface SharedLoan {
  readonly id: string;
  readonly mediaId: string;
  readonly mediaType: MediaType;
  readonly mediaTitle: string;
  readonly status: LoanStatus;
  readonly requestedAt?: string | null;
  readonly lentAt?: string | null;
  readonly closedAt?: string | null;
}

export const sharedLoanStatusLabels: Readonly<Record<LoanStatus, string>> = {
  Requested: 'En attente',
  Active: 'Prêté',
  Returned: 'Rendu',
  Declined: 'Refusé',
  Cancelled: 'Retiré',
};

export interface SharedMediaQuery {
  readonly type?: MediaType | null;
  readonly q?: string | null;
  readonly sort?: MediaSort | null;
  readonly page?: number;
  readonly size?: number;
}

/**
 * Refus de Tenants.Api autour du partage. Un code d'invitation refusé
 * arrive en 400 avec son explication, qu'on rend telle quelle ; le reste
 * suit les refus ordinaires.
 */
export function describeSharingError(error: unknown): ApiProblem {
  if (error instanceof HttpErrorResponse) {
    const detail = codeError(error) ?? problemDetail(error);

    if (error.status === 400 && detail) {
      return { kind: 'autre', message: detail, terminal: false };
    }
    if (error.status === 404) {
      return {
        kind: 'introuvable',
        message: "Ce catalogue n'est plus partagé avec votre ménage.",
        terminal: false,
      };
    }
    if (error.status === 429) {
      return {
        kind: 'autre',
        message: 'Trop d’essais en peu de temps. Patientez une minute avant de réessayer.',
        terminal: false,
      };
    }
    if ((error.status === 409 || error.status === 503) && detail) {
      return { kind: error.status === 409 ? 'conflit' : 'autre', message: detail, terminal: false };
    }
  }

  return describeHttpError(error);
}

function codeError(error: HttpErrorResponse): string | null {
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null || !('errors' in body)) {
    return null;
  }

  const errors = (body as { errors?: unknown }).errors;
  if (typeof errors !== 'object' || errors === null || !('code' in errors)) {
    return null;
  }

  const messages = (errors as { code?: unknown }).code;
  return Array.isArray(messages) && typeof messages[0] === 'string' ? messages[0] : null;
}

function problemDetail(error: HttpErrorResponse): string | null {
  const body: unknown = error.error;
  if (typeof body === 'object' && body !== null && 'detail' in body) {
    const detail = (body as { detail?: unknown }).detail;
    return typeof detail === 'string' && detail.length > 0 ? detail : null;
  }
  return null;
}
