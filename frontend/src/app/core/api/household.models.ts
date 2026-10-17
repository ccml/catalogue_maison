import { HttpErrorResponse } from '@angular/common/http';
import { describeMemberError, type MemberProblem } from './member.models';

/**
 * Contrats de Tenants.Api, administration des ménages par l'opérateur.
 *
 * C'est le seul endroit du frontend où l'identifiant d'un ménage circule :
 * l'opérateur n'appartient à aucun ménage, il les administre. Un membre ne
 * voit jamais cet identifiant.
 */

export type HouseholdStatus = 'Provisioning' | 'Active' | 'Archiving' | 'Archived' | 'Purging';

/** Étiquette affichée à côté d'un ménage ; rien pour un ménage actif. */
export const householdStatusLabels: Readonly<Record<HouseholdStatus, string | null>> = {
  Provisioning: 'Création inachevée',
  Active: null,
  Archiving: 'Archivage inachevé',
  Archived: 'Archivé',
  Purging: 'Suppression inachevée',
};

export interface Household {
  /** Immuable : nom du schéma du catalogue et valeur du claim `tenant`. */
  readonly id: string;
  readonly displayName: string;
  readonly createdAt: string;
  readonly status: HouseholdStatus;
  readonly memberCount: number;
  readonly archivedAt: string | null;
}

export interface CreateHouseholdRequest {
  readonly id: string;
  readonly displayName: string;
}

/** Toujours adulte et administrateur : l'API ne laisse pas choisir. */
export interface CreateHouseholdAdminRequest {
  readonly username: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly temporaryPassword: string;
}

export const householdIdMaxLength = 48;

/** Même règle que `TenantId` côté API, qui reste seule juge. */
export const householdIdPattern = /^[a-z][a-z0-9_]*$/;

/**
 * Propose un identifiant à partir du nom affiché : « Famille Lefèvre-Dupont »
 * donne `famille_lefevre_dupont`. Ce n'est qu'une suggestion, que l'opérateur
 * peut corriger avant la création — après, l'identifiant ne change plus.
 */
export function suggestHouseholdId(displayName: string): string {
  const slug = displayName
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');

  const startsWithLetter = /^[a-z]/.test(slug) ? slug : slug.length > 0 ? `m_${slug}` : '';
  // Les préfixes réservés par PostgreSQL ne sont jamais proposés.
  const safe = startsWithLetter.startsWith('pg_') ? `m_${startsWithLetter}` : startsWithLetter;
  return safe.slice(0, householdIdMaxLength).replace(/_+$/, '');
}

const messages: Readonly<Record<string, string>> = {
  'menage-existant': 'Un ménage porte déjà cet identifiant. Choisissez-en un autre.',
  'menage-introuvable': "Ce ménage n'existe pas, ou plus.",
  'catalogue-indisponible':
    "Le catalogue du ménage n'a pas répondu. Relancez l'opération : elle reprendra où elle s'est arrêtée.",
  'menage-non-inscrit':
    "La création de ce ménage n'est pas terminée : reprenez-la avant d'y ajouter quelqu'un.",
  'menage-non-archive': "Ce ménage doit d'abord être archivé.",
  'menage-en-creation':
    "Ce ménage n'a jamais été actif : reprenez sa création, ou supprimez-le directement.",
  'suppression-en-cours': 'Ce ménage est en cours de suppression : reprenez la suppression.',
};

export function describeHouseholdError(error: unknown): MemberProblem {
  if (error instanceof HttpErrorResponse) {
    const body: unknown = error.error;
    const type =
      typeof body === 'object' && body !== null && 'type' in body
        ? (body as { type?: unknown }).type
        : undefined;
    const code = typeof type === 'string' ? type.split('/').pop() : undefined;

    if (code !== undefined && code in messages) {
      return { message: messages[code], fields: {} };
    }

    if (error.status === 403) {
      return { message: "Votre compte n'administre pas les ménages.", fields: {} };
    }
  }

  return describeMemberError(error);
}
