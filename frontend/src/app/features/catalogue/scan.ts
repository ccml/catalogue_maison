import {
  mediaTypeLabels,
  type FilmSearchResult,
  type MediaType,
  type ScanResult,
} from '../../core/api/media.models';

/** Type proposé par la fiche, en minuscules pour la phrase : « livre », « BD ». */
function typeLabel(result: ScanResult): string {
  const label = mediaTypeLabels[result.draft?.type ?? 'Book'];
  return label === 'BD' ? label : label.toLowerCase();
}

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

/** Vrai pour un ISBN : 10 caractères, ou EAN-13 du préfixe livre 978 / 979. */
export function isIsbn(code: string): boolean {
  return code.length === 10 || (code.length === 13 && /^97[89]/.test(code));
}

/** Libellés des sources d'enrichissement. L'API renvoie leur identifiant. */
const sourceLabels: Readonly<Record<string, string>> = {
  bnf: 'la Bibliothèque nationale de France',
  openlibrary: 'Open Library',
  musicbrainz: 'MusicBrainz',
  omdb: 'OMDb',
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
export function scanNotice(result: ScanResult, chosen?: MediaType | null): ScanNotice {
  const notice = describeScan(result);
  const found = result.draft?.type;

  // Parti de « Livre », on peut scanner une BD : la fiche suit la notice,
  // et on le dit plutôt que de changer le titre de l'écran sans prévenir.
  return chosen && found && found !== chosen && !result.existing
    ? { ...notice, text: `${notice.text} La notice décrit ${articleFor(found)} : le type a été changé.` }
    : notice;
}

function articleFor(type: MediaType): string {
  const label = mediaTypeLabels[type];
  return type === 'ComicBook' ? `une ${label}` : `un ${label.toLowerCase()}`;
}

function describeScan(result: ScanResult): ScanNotice {
  const isbnNote = result.draft?.specifics?.type === 'book' ? ' L’ISBN est déjà noté.' : '';

  switch (result.enrichment) {
    case 'Found':
      if (result.provisional) {
        // On ne devine pas le type à la place de la source : on prévient.
        return {
          text: `Fiche pré-remplie d’après une notice provisoire de ${sourceLabel(result.source)}, établie avant catalogage : elle ne précise pas toujours le type. Vérifiez qu’il s’agit bien d’un ${typeLabel(result)}, puis enregistrez.`,
          tone: 'attention',
        };
      }
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

/** Phrase affichée après une recherche de films par titre. */
export function filmSearchNotice(result: FilmSearchResult): ScanNotice | null {
  if (result.unavailable) {
    return {
      text: 'La recherche de films ne répond pas pour l’instant. Vous pouvez compléter la fiche à la main.',
      tone: 'attention',
    };
  }

  return result.films.length === 0
    ? { text: 'Aucun film ne correspond à ce titre.', tone: 'attention' }
    : null;
}

/**
 * Phrase affichée après le choix d'un film. OMDb décrit les films en
 * anglais : on le dit, pour que l'utilisateur corrige le titre s'il préfère
 * le titre français.
 */
export function filmDraftNotice(source: string): ScanNotice {
  return {
    text: `Fiche pré-remplie d’après ${sourceLabel(source)}, qui donne titre et genre en anglais. Vérifiez-la, indiquez où le film est rangé, puis enregistrez.`,
    tone: 'info',
  };
}
