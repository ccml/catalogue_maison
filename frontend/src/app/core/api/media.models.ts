/**
 * Contrats HTTP de Catalogue.Api, transcrits tels quels.
 *
 * Le discriminant des spécificités (`type`, en minuscules) est celui du
 * stockage côté API : il ne se renomme pas d'un côté sans l'autre.
 */

export type MediaType = 'Book' | 'ComicBook' | 'Music' | 'Film' | 'Game';

export interface BookSpecifics {
  readonly type: 'book';
  readonly isbn?: string | null;
  readonly publisher?: string | null;
  readonly pages?: number | null;
}

/** Une BD se range par série et tome : c'est ainsi qu'on la cherche. */
export interface ComicBookSpecifics {
  readonly type: 'comic';
  readonly series?: string | null;
  readonly volume?: number | null;
  readonly illustrator?: string | null;
  readonly publisher?: string | null;
  readonly isbn?: string | null;
}

export interface MusicSpecifics {
  readonly type: 'music';
  readonly label?: string | null;
  readonly format?: string | null;
  readonly tracks?: readonly string[];
}

export interface FilmSpecifics {
  readonly type: 'film';
  readonly studio?: string | null;
  readonly durationMinutes?: number | null;
  readonly support?: string | null;
}

export interface GameSpecifics {
  readonly type: 'game';
  readonly platform?: string | null;
  readonly minPlayers?: number | null;
  readonly maxPlayers?: number | null;
}

export type MediaSpecifics =
  | BookSpecifics
  | ComicBookSpecifics
  | MusicSpecifics
  | FilmSpecifics
  | GameSpecifics;

export interface MediaSummary {
  readonly id: string;
  readonly type: MediaType;
  readonly title: string;
  readonly creator: string | null;
  readonly year: number | null;
  readonly location: string | null;
  readonly isLent: boolean;
  readonly addedAt: string;
}

export interface MediaDetail extends MediaSummary {
  readonly specifics: MediaSpecifics | null;
}

export interface Paged<T> {
  readonly items: readonly T[];
  readonly total: number;
  readonly page: number;
  readonly size: number;
}

export interface CreateMediaRequest {
  readonly type: MediaType;
  readonly title: string;
  readonly creator?: string | null;
  readonly year?: number | null;
  readonly location?: string | null;
  readonly specifics?: MediaSpecifics | null;
}

/**
 * Correction d'une fiche existante. Même forme que la création, y compris le
 * type : l'API accepte qu'on le change.
 */
export type UpdateMediaRequest = CreateMediaRequest;

export interface MediaQuery {
  readonly type?: MediaType | null;
  readonly q?: string | null;
  readonly lent?: boolean | null;
  readonly page?: number;
  readonly size?: number;
}

/**
 * Les types, dans l'ordre d'affichage. **Source unique** : les déclarer dans
 * chaque écran garantit qu'un nouveau type sera oublié quelque part.
 */
export const mediaTypes: readonly MediaType[] = ['Book', 'ComicBook', 'Music', 'Film', 'Game'];

/** Libellés affichés. Le code reste en anglais, l'interface parle français. */
export const mediaTypeLabels: Readonly<Record<MediaType, string>> = {
  Book: 'Livre',
  ComicBook: 'BD',
  Music: 'Musique',
  Film: 'Film',
  Game: 'Jeu',
};

/** Spécificités vides d'un type donné, pour amorcer un formulaire. */
export function emptySpecifics(type: MediaType): MediaSpecifics {
  switch (type) {
    case 'Book':
      return { type: 'book' };
    case 'ComicBook':
      return { type: 'comic' };
    case 'Music':
      return { type: 'music' };
    case 'Film':
      return { type: 'film' };
    case 'Game':
      return { type: 'game' };
  }
}

/** Ce que l'enrichissement a pu dire d'un code scanné. */
export type EnrichmentStatus = 'Found' | 'NotFound' | 'Unavailable' | 'NotAttempted';

/**
 * Fiche pré-remplie par un scan. Rien n'est enregistré : l'utilisateur
 * valide ou corrige, puis enregistre comme une saisie manuelle.
 *
 * L'API omet les champs nuls : chacun est donc facultatif ici, et se lit
 * avec `?? null`.
 */
export interface ScanDraft {
  readonly type: MediaType;
  readonly title?: string | null;
  readonly creator?: string | null;
  readonly year?: number | null;
  readonly specifics?: MediaSpecifics | null;
}

export interface ScanResult {
  /** Code tel que l'API l'a normalisé. */
  readonly code: string;
  /** Média du ménage portant déjà ce code. Absent s'il n'y en a pas. */
  readonly existing?: MediaSummary | null;
  /** Absente si le média existe déjà ou si rien n'est connu du code. */
  readonly draft?: ScanDraft | null;
  readonly enrichment: EnrichmentStatus;
  /** Identifiant de la source de la notice : `bnf`, `openlibrary`, `musicbrainz`. */
  readonly source?: string | null;
}
