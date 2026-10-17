import type { ScanResult } from '../../core/api/media.models';

/**
 * Code saisi ou lu, débarrassé de ses séparateurs, ou `null` s'il n'a pas la
 * forme d'un ISBN (10 ou 13) ou d'un code-barres (EAN-8, UPC-A, EAN-13).
 *
 * La clé de contrôle n'est pas vérifiée ici : un code mal imprimé, tapé à la
 * main, doit pouvoir être cherché. Le lecteur caméra, lui, la vérifie.
 */
export function normalizeCode(raw: string): string | null {
  const compact = raw.replace(/[\s-]/g, '').toUpperCase();

  if (!/^\d{7,12}[\dX]$/.test(compact)) {
    return null;
  }

  // Le X n'existe qu'en dernière position d'un ISBN-10.
  if (compact.endsWith('X') && compact.length !== 10) {
    return null;
  }

  return [8, 10, 12, 13].includes(compact.length) ? compact : null;
}

/** Libellés des sources d'enrichissement. L'API renvoie leur identifiant. */
const sourceLabels: Readonly<Record<string, string>> = {
  bnf: 'la Bibliothèque nationale de France',
  openlibrary: 'Open Library',
  musicbrainz: 'MusicBrainz',
};

export function sourceLabel(source: string | null | undefined): string {
  return source == null ? 'une source externe' : (sourceLabels[source] ?? source);
}

/** Ce qu'il faut dire à l'utilisateur après un scan. */
export interface ScanNotice {
  readonly text: string;
  /** `info` : la fiche est prête à vérifier. `attention` : il reste à faire. */
  readonly tone: 'info' | 'attention';
}

/**
 * Phrase affichée après un scan. Chaque issue dit ce qui s'est passé **et**
 * ce qu'il reste à faire : aucune ne doit laisser croire que l'ajout est
 * bloqué, puisqu'il ne l'est jamais.
 */
export function scanNotice(result: ScanResult): ScanNotice {
  const isbnNote = result.draft?.specifics?.type === 'book' ? ' L’ISBN est déjà noté.' : '';

  switch (result.enrichment) {
    case 'Found':
      return {
        text: `Fiche pré-remplie d’après ${sourceLabel(result.source)}. Vérifiez-la, indiquez où elle est rangée, puis enregistrez.`,
        tone: 'info',
      };

    case 'NotFound':
      return {
        text: `Aucune notice ne correspond à ce code.${isbnNote} Complétez la fiche à la main.`,
        tone: 'attention',
      };

    case 'Unavailable':
      return {
        text: `La recherche de notice ne répond pas pour l’instant.${isbnNote} Vous pouvez compléter la fiche à la main, ou réessayer plus tard.`,
        tone: 'attention',
      };

    case 'NotAttempted':
      return {
        text: 'Ce média est déjà au catalogue.',
        tone: 'attention',
      };
  }
}
