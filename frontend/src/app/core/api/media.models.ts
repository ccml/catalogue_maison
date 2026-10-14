/**
 * Contrats HTTP de Catalogue.Api, transcrits tels quels.
 *
 * Le discriminant des spécificités (`type`, en minuscules) est celui du
 * stockage côté API : il ne se renomme pas d'un côté sans l'autre.
 */

export type MediaType = 'Book' | 'Music' | 'Film' | 'Game';

export interface BookSpecifics {
  readonly type: 'book';
  readonly isbn?: string | null;
  readonly publisher?: string | null;
  readonly pages?: number | null;
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

export type MediaSpecifics = BookSpecifics | MusicSpecifics | FilmSpecifics | GameSpecifics;

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

export interface MediaQuery {
  readonly type?: MediaType | null;
  readonly q?: string | null;
  readonly lent?: boolean | null;
  readonly page?: number;
  readonly size?: number;
}

/** Libellés affichés. Le code reste en anglais, l'interface parle français. */
export const mediaTypeLabels: Readonly<Record<MediaType, string>> = {
  Book: 'Livre',
  Music: 'Musique',
  Film: 'Film',
  Game: 'Jeu',
};

/** Spécificités vides d'un type donné, pour amorcer un formulaire. */
export function emptySpecifics(type: MediaType): MediaSpecifics {
  switch (type) {
    case 'Book':
      return { type: 'book' };
    case 'Music':
      return { type: 'music' };
    case 'Film':
      return { type: 'film' };
    case 'Game':
      return { type: 'game' };
  }
}
