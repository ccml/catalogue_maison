import { HttpErrorResponse } from '@angular/common/http';

/** Contrats de Tenants.Api, administration des membres. */

export type MemberRole = 'Adult' | 'Child';

export const memberRoleLabels: Readonly<Record<MemberRole, string>> = {
  Adult: 'Adulte',
  Child: 'Enfant',
};

export interface Member {
  readonly id: string;
  readonly username: string;
  readonly firstName: string | null;
  readonly lastName: string | null;
  readonly role: MemberRole;
  readonly isAdmin: boolean;
  readonly enabled: boolean;
  /** Faux : compte rattaché au ménage dans Keycloak mais absent du registre. */
  readonly registered: boolean;
  /** Le compte de la personne connectée. */
  readonly isSelf: boolean;
}

/** Aucun champ ne désigne le ménage : l'API prend celui du jeton. */
export interface CreateMemberRequest {
  readonly username: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly role: MemberRole;
  readonly isAdmin: boolean;
  readonly temporaryPassword: string;
}

export interface UpdateMemberRoleRequest {
  readonly role: MemberRole;
  readonly isAdmin: boolean;
}

export const temporaryPasswordMinLength = 8;

export type OrphanReason = 'AccountMissing' | 'AccountDetached';

export const orphanReasonLabels: Readonly<Record<OrphanReason, string>> = {
  AccountMissing: "Le compte n'existe plus dans le service de connexion.",
  AccountDetached: "Le compte existe encore, mais n'est plus rattaché à ce ménage.",
};

/** Entrée du registre sans compte rattaché au ménage. */
export interface OrphanEntry {
  readonly subject: string;
  readonly username: string;
  readonly registeredAt: string;
  readonly reason: OrphanReason;
}

/** Écarts entre le service de connexion et le registre du ménage. */
export interface Inconsistencies {
  readonly unregisteredAccounts: readonly Member[];
  readonly orphanEntries: readonly OrphanEntry[];
}

/** Refus de Tenants.Api, rendu affichable. */
export interface MemberProblem {
  readonly message: string;
  /** Erreurs par champ du formulaire, quand l'API les détaille. */
  readonly fields: Readonly<Record<string, string>>;
}

/** Codes stables des refus de Tenants.Api (fin du `type` du problem details). */
const messages: Readonly<Record<string, string>> = {
  'identifiant-pris': 'Cet identifiant est déjà utilisé. Choisissez-en un autre.',
  'menage-non-inscrit':
    "Votre ménage n'est pas actif — sa création n'est pas terminée, ou il a été archivé : ses membres ne peuvent pas être modifiés.",
  'registre-indisponible':
    "Le compte est à jour, mais pas le registre du ménage. Relancez la même opération : elle reprendra où elle s'est arrêtée.",
  'keycloak-indisponible': 'Le service de connexion ne répond pas. Réessayez dans un instant.',
  'membre-introuvable': "Ce membre n'existe pas, ou plus, dans votre ménage.",
  'inscrit-ailleurs':
    "Ce compte est encore inscrit au registre d'un autre ménage. Son administrateur doit d'abord retirer cette entrée.",
};

export function describeMemberError(error: unknown): MemberProblem {
  if (!(error instanceof HttpErrorResponse)) {
    return { message: "L'administration du ménage n'a pas répondu comme prévu.", fields: {} };
  }

  if (error.status === 0) {
    return {
      message: "L'administration du ménage est injoignable. Vérifiez qu'elle est démarrée.",
      fields: {},
    };
  }

  const body = readBody(error);

  if (error.status === 400 && body?.errors) {
    const fields: Record<string, string> = {};
    for (const [field, problems] of Object.entries(body.errors)) {
      if (problems.length > 0) {
        fields[field] = problems[0];
      }
    }
    return { message: 'Vérifiez les champs signalés.', fields };
  }

  const code = body?.type?.split('/').pop();
  if (code !== undefined && code in messages) {
    return { message: messages[code], fields: {} };
  }

  // Les autres refus métier (se retirer soi-même l'administration…) sont
  // déjà formulés par l'API pour être lus.
  if (error.status === 409 && body?.detail) {
    return { message: body.detail, fields: {} };
  }

  switch (error.status) {
    case 401:
      return { message: 'Votre session a expiré. Reconnectez-vous pour continuer.', fields: {} };
    case 403:
      return { message: "Votre compte n'administre pas ce ménage.", fields: {} };
    default:
      return {
        message: `L'administration du ménage a répondu une erreur ${error.status}.`,
        fields: {},
      };
  }
}

interface ProblemBody {
  readonly type?: string;
  readonly detail?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

function readBody(error: HttpErrorResponse): ProblemBody | null {
  const body: unknown = typeof error.error === 'string' ? safeParse(error.error) : error.error;
  return typeof body === 'object' && body !== null ? (body as ProblemBody) : null;
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}
