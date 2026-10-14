import { type MediaSpecifics, type MediaType } from '../../core/api/media.models';

/** Champs de spécificités du formulaire, tous types confondus. */
export interface SpecificsFields {
  readonly isbn: string;
  readonly publisher: string;
  readonly pages: number | null;
  readonly label: string;
  readonly format: string;
  readonly studio: string;
  readonly durationMinutes: number | null;
  readonly support: string;
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
      const isbn = text(fields.isbn);
      const publisher = text(fields.publisher);
      const pages = number(fields.pages);
      return isbn === null && publisher === null && pages === null
        ? null
        : { type: 'book', isbn, publisher, pages };
    }

    case 'Music': {
      const label = text(fields.label);
      const format = text(fields.format);
      return label === null && format === null ? null : { type: 'music', label, format };
    }

    case 'Film': {
      const studio = text(fields.studio);
      const durationMinutes = number(fields.durationMinutes);
      const support = text(fields.support);
      return studio === null && durationMinutes === null && support === null
        ? null
        : { type: 'film', studio, durationMinutes, support };
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
