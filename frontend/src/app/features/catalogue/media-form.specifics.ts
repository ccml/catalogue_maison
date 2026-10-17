import { type MediaSpecifics, type MediaType } from '../../core/api/media.models';

/** Champs de spécificités du formulaire, tous types confondus. */
export interface SpecificsFields {
  readonly isbn: string;
  readonly publisher: string;
  readonly pages: number | null;
  readonly collection: string;
  readonly collectionNumber: string;
  readonly genre: string;
  readonly edition: string;
  readonly series: string;
  readonly volume: number | null;
  readonly illustrator: string;
  readonly label: string;
  readonly format: string;
  /** Une piste par ligne. */
  readonly tracks: string;
  readonly barcode: string;
  readonly studio: string;
  readonly durationMinutes: number | null;
  readonly support: string;
  /** Un acteur par ligne. */
  readonly actors: string;
  readonly platform: string;
  readonly minPlayers: number | null;
  readonly maxPlayers: number | null;
}

/**
 * Construit les spécificités du type demandé, et de lui seul.
 *
 * Deux règles, et c'est tout l'intérêt de les isoler ici :
 *
 * - un champ renseigné pour un autre type est **ignoré**. Sans cela, changer
 *   le type dans le formulaire enverrait les restes de la saisie précédente,
 *   et l'API les refuserait.
 * - si aucun champ du type ne porte de valeur, le résultat est `null` plutôt
 *   qu'un objet vide : on n'écrit pas de document JSON sans contenu.
 */
export function buildSpecifics(type: MediaType, fields: SpecificsFields): MediaSpecifics | null {
  switch (type) {
    case 'Book': {
      const book = {
        type: 'book',
        isbn: text(fields.isbn),
        publisher: text(fields.publisher),
        pages: number(fields.pages),
        series: text(fields.series),
        volume: number(fields.volume),
        collection: text(fields.collection),
        collectionNumber: text(fields.collectionNumber),
        genre: text(fields.genre),
      } as const;
      return Object.entries(book).every(([key, value]) => key === 'type' || value === null)
        ? null
        : book;
    }

    case 'ComicBook': {
      const comic = {
        type: 'comic',
        series: text(fields.series),
        volume: number(fields.volume),
        illustrator: text(fields.illustrator),
        publisher: text(fields.publisher),
        isbn: text(fields.isbn),
        collection: text(fields.collection),
        collectionNumber: text(fields.collectionNumber),
        edition: text(fields.edition),
      } as const;
      return Object.entries(comic).every(([key, value]) => key === 'type' || value === null)
        ? null
        : comic;
    }

    case 'Music': {
      const label = text(fields.label);
      const format = text(fields.format);
      const tracks = parseLines(fields.tracks);
      const barcode = text(fields.barcode);
      return label === null && format === null && tracks.length === 0 && barcode === null
        ? null
        : { type: 'music', label, format, tracks, barcode };
    }

    case 'Film': {
      const studio = text(fields.studio);
      const durationMinutes = number(fields.durationMinutes);
      const support = text(fields.support);
      const genre = text(fields.genre);
      const actors = parseLines(fields.actors);
      return studio === null &&
        durationMinutes === null &&
        support === null &&
        genre === null &&
        actors.length === 0
        ? null
        : { type: 'film', studio, durationMinutes, support, genre, actors };
    }

    case 'Game': {
      const platform = text(fields.platform);
      const minPlayers = number(fields.minPlayers);
      const maxPlayers = number(fields.maxPlayers);
      return platform === null && minPlayers === null && maxPlayers === null
        ? null
        : { type: 'game', platform, minPlayers, maxPlayers };
    }
  }
}

/** Texte saisi, ou `null` si l'utilisateur n'a rien mis. */
export function text(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

function number(value: number | null): number | null {
  return value === null || Number.isNaN(value) ? null : value;
}

/**
 * Lit un nombre saisi au clavier. Les champs numériques du formulaire sont
 * des champs texte : un `input[type=number]` rendrait le contrôle nullable
 * et laisserait passer des saisies partielles.
 */
export function parseNumber(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return null;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Une piste par ligne, les lignes vides ignorées. */
export function parseLines(value: string): readonly string[] {
  return value
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

const champsVides: SpecificsFields = {
  isbn: '',
  publisher: '',
  pages: null,
  collection: '',
  collectionNumber: '',
  genre: '',
  edition: '',
  series: '',
  volume: null,
  illustrator: '',
  label: '',
  format: '',
  tracks: '',
  barcode: '',
  studio: '',
  durationMinutes: null,
  support: '',
  actors: '',
  platform: '',
  minPlayers: null,
  maxPlayers: null,
};

/**
 * Remplit les champs du formulaire depuis des spécificités existantes.
 *
 * C'est la réciproque de {@link buildSpecifics}, et elle doit le rester : un
 * champ présent en base mais absent d'ici serait **effacé** à la première
 * correction de la fiche, sans que rien ne le signale. L'aller-retour est
 * testé pour cette raison.
 */
export function specificsToFields(specifics: MediaSpecifics | null): SpecificsFields {
  if (specifics === null) {
    return champsVides;
  }

  switch (specifics.type) {
    case 'book':
      return {
        ...champsVides,
        isbn: specifics.isbn ?? '',
        publisher: specifics.publisher ?? '',
        pages: specifics.pages ?? null,
        series: specifics.series ?? '',
        volume: specifics.volume ?? null,
        collection: specifics.collection ?? '',
        collectionNumber: specifics.collectionNumber ?? '',
        genre: specifics.genre ?? '',
      };

    case 'comic':
      return {
        ...champsVides,
        series: specifics.series ?? '',
        volume: specifics.volume ?? null,
        illustrator: specifics.illustrator ?? '',
        publisher: specifics.publisher ?? '',
        isbn: specifics.isbn ?? '',
        collection: specifics.collection ?? '',
        collectionNumber: specifics.collectionNumber ?? '',
        edition: specifics.edition ?? '',
      };

    case 'music':
      return {
        ...champsVides,
        label: specifics.label ?? '',
        format: specifics.format ?? '',
        tracks: (specifics.tracks ?? []).join('\n'),
        barcode: specifics.barcode ?? '',
      };

    case 'film':
      return {
        ...champsVides,
        studio: specifics.studio ?? '',
        durationMinutes: specifics.durationMinutes ?? null,
        support: specifics.support ?? '',
        genre: specifics.genre ?? '',
        actors: (specifics.actors ?? []).join('\n'),
      };

    case 'game':
      return {
        ...champsVides,
        platform: specifics.platform ?? '',
        minPlayers: specifics.minPlayers ?? null,
        maxPlayers: specifics.maxPlayers ?? null,
      };
  }
}
