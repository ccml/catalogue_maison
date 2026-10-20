import { HttpErrorResponse } from '@angular/common/http';
import type { CatalogueExport } from '../../core/api/media.models';

/** Valeur du champ `format` d'un export, fixée par Catalogue.Api. */
export const exportFormat = 'cataloguemaison';

/** Nom du fichier téléchargé : daté, pour qu'une série d'exports se range d'elle-même. */
export function exportFileName(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `catalogue-${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}.json`;
}

export type ParsedExport =
  | { readonly ok: true; readonly file: CatalogueExport }
  | { readonly ok: false; readonly message: string };

/**
 * Premier contrôle d'un fichier choisi pour l'import : est-ce bien un export
 * de CatalogueMaison ? Simple confort, pour répondre avant l'envoi et
 * annoncer le nombre de fiches — c'est l'API qui valide chaque fiche.
 */
export function parseExport(text: string): ParsedExport {
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return { ok: false, message: 'Ce fichier n’est pas un fichier JSON lisible.' };
  }

  if (
    typeof value !== 'object' ||
    value === null ||
    !('format' in value) ||
    value.format !== exportFormat ||
    !('medias' in value) ||
    !Array.isArray(value.medias)
  ) {
    return { ok: false, message: 'Ce fichier n’est pas un export de Catalogue maison.' };
  }

  return { ok: true, file: value as CatalogueExport };
}

/**
 * Messages d'un refus de l'import (400, problem details de validation) :
 * l'API y nomme chaque fiche fautive. Liste vide pour toute autre erreur,
 * qui relève de `describeHttpError`.
 */
export function importErrors(error: unknown): readonly string[] {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return [];
  }

  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null || !('errors' in body)) {
    return [];
  }

  const errors: unknown = body.errors;
  if (typeof errors !== 'object' || errors === null) {
    return [];
  }

  return Object.values(errors)
    .filter((messages): messages is unknown[] => Array.isArray(messages))
    .flat()
    .filter((message): message is string => typeof message === 'string');
}
