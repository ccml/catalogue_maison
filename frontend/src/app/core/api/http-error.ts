import { HttpErrorResponse } from '@angular/common/http';

/**
 * Nature du refus, telle que l'API la désigne.
 *
 * La distinction qui compte est `sessionExpiree` contre `menageAbsent` : les
 * deux arrivent en 401, mais l'une se répare en se reconnectant et l'autre
 * jamais. Les confondre envoie l'utilisateur tourner en rond.
 */
export type ApiProblemKind =
  | 'reseau'
  | 'sessionExpiree'
  | 'menageAbsent'
  | 'interdit'
  | 'introuvable'
  | 'schemaEnRetard'
  | 'autre';

export interface ApiProblem {
  readonly kind: ApiProblemKind;
  /** Phrase affichable : ce qui s'est passé, et ce qu'il y a à faire. */
  readonly message: string;
  /**
   * Vrai quand se reconnecter n'y changera rien. L'interface ne doit alors
   * pas le proposer.
   */
  readonly terminal: boolean;
}

/** Types de problème émis par Catalogue.Api. */
const problemTypes = {
  tenantAbsent: 'tenant-absent',
  schemaEnRetard: 'schema-en-retard',
} as const;

export function describeHttpError(error: unknown): ApiProblem {
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'autre', message: "Le catalogue n'a pas répondu comme prévu.", terminal: false };
  }

  const type = readProblemType(error);

  if (error.status === 0) {
    return {
      kind: 'reseau',
      message: "Le catalogue est injoignable. Vérifiez qu'il est démarré.",
      terminal: false,
    };
  }

  if (error.status === 401) {
    // Un jeton valide mais sans ménage exploitable : l'API refuse, et elle a
    // raison. Aucune reconnexion ne rattachera ce compte à un ménage.
    if (type?.endsWith(problemTypes.tenantAbsent) === true) {
      return {
        kind: 'menageAbsent',
        message:
          "Votre compte n'est rattaché à aucun ménage, il n'y a donc aucun catalogue à vous montrer. Demandez à la personne qui administre votre ménage de vous y ajouter.",
        terminal: true,
      };
    }

    return {
      kind: 'sessionExpiree',
      message: 'Votre session a expiré. Reconnectez-vous pour continuer.',
      terminal: false,
    };
  }

  if (error.status === 503 && type?.endsWith(problemTypes.schemaEnRetard) === true) {
    return {
      kind: 'schemaEnRetard',
      message:
        "Le catalogue de votre ménage n'est pas à jour et ne peut pas être servi. Prévenez la personne qui l'administre.",
      terminal: true,
    };
  }

  switch (error.status) {
    case 403:
      return {
        kind: 'interdit',
        message: "Votre compte n'a pas le droit de faire cela.",
        terminal: true,
      };
    case 404:
      return {
        kind: 'introuvable',
        message: 'Introuvable dans le catalogue du ménage.',
        terminal: false,
      };
    case 503:
      return {
        kind: 'autre',
        message: 'Le catalogue est momentanément indisponible.',
        terminal: false,
      };
    default:
      return {
        kind: 'autre',
        message: `Le catalogue a répondu une erreur ${error.status}.`,
        terminal: false,
      };
  }
}

/**
 * Lit le `type` du problem details. Angular livre le corps déjà analysé la
 * plupart du temps, mais pas toujours : on accepte les deux formes plutôt
 * que de perdre la seule information qui distingue les deux 401.
 */
function readProblemType(error: HttpErrorResponse): string | null {
  const body: unknown = error.error;

  if (typeof body === 'object' && body !== null && 'type' in body) {
    const type = (body as { type?: unknown }).type;
    return typeof type === 'string' ? type : null;
  }

  if (typeof body === 'string' && body.length > 0) {
    try {
      const analyse: unknown = JSON.parse(body);
      if (typeof analyse === 'object' && analyse !== null && 'type' in analyse) {
        const type = (analyse as { type?: unknown }).type;
        return typeof type === 'string' ? type : null;
      }
    } catch {
      return null;
    }
  }

  return null;
}
