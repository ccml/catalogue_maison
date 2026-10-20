/**
 * Contrats HTTP de Catalogue.Api, transcrits tels quels.
 *
 * Le discriminant des spécificités (`type`, en minuscules) est celui du
 * stockage côté API : il ne se renomme pas d'un côté sans l'autre.
 */

export type MediaType = 'Book' | 'ComicBook' | 'Music' | 'Film' | 'Game';

/**
 * Un livre. La série (Harry Potter, tome 3) appartient à l'œuvre, la
 * collection (Folio policier, n° 85) à l'édition : ce sont deux champs.
 */
export interface BookSpecifics {
  readonly type: 'book';
  readonly isbn?: string | null;
  readonly publisher?: string | null;
  readonly pages?: number | null;
  readonly series?: string | null;
  readonly volume?: number | null;
  readonly collection?: string | null;
  /** Du texte : certaines collections numérotent « LP 3121 ». */
  readonly collectionNumber?: string | null;
  readonly genre?: string | null;
}

/** Une BD se range par série et tome : c'est ainsi qu'on la cherche. */
export interface ComicBookSpecifics {
  readonly type: 'comic';
  readonly series?: string | null;
  readonly volume?: number | null;
  readonly illustrator?: string | null;
  readonly publisher?: string | null;
  readonly isbn?: string | null;
  readonly collection?: string | null;
  readonly collectionNumber?: string | null;
  /** Mention d'édition : « Éd. de luxe », « Nouvelle éd. ». */
  readonly edition?: string | null;
}

export interface MusicSpecifics {
  readonly type: 'music';
  readonly label?: string | null;
  readonly format?: string | null;
  readonly tracks?: readonly string[];
  /** Code-barres du disque, renvoyé sous sa forme EAN-13. Clé des doublons au scan. */
  readonly barcode?: string | null;
}

export interface FilmSpecifics {
  readonly type: 'film';
  readonly studio?: string | null;
  readonly durationMinutes?: number | null;
  readonly support?: string | null;
  /** Tel qu'OMDb le donne, en anglais : « Action, Sci-Fi ». */
  readonly genre?: string | null;
  /** Le réalisateur, lui, est dans `creator`. */
  readonly actors?: readonly string[];
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

/**
 * L'API omet les champs nuls : un média saisi avec son seul titre arrive sans
 * `creator`, `year` ni `location`. Ces champs sont donc facultatifs, et se
 * lisent avec `?? null`.
 */
export interface MediaSummary {
  readonly id: string;
  readonly type: MediaType;
  readonly title: string;
  readonly creator?: string | null;
  readonly year?: number | null;
  readonly location?: string | null;
  readonly isLent: boolean;
  readonly addedAt: string;
  /** Emprunteur du prêt en cours, quand `isLent`. */
  readonly lentTo?: string | null;
}

export interface MediaDetail extends MediaSummary {
  readonly specifics?: MediaSpecifics | null;
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
  readonly sort?: MediaSort | null;
  readonly page?: number;
  readonly size?: number;
}

/** Ordre de la liste. `Recent` : derniers ajoutés d'abord, l'ordre par défaut de l'API. */
export type MediaSort = 'Recent' | 'Title' | 'Creator' | 'Series';

export const mediaSorts: readonly MediaSort[] = ['Recent', 'Title', 'Creator', 'Series'];

export const mediaSortLabels: Readonly<Record<MediaSort, string>> = {
  Recent: 'Derniers ajouts',
  Title: 'Titre',
  Creator: 'Auteur, artiste',
  Series: 'Série et tome',
};

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

/**
 * Segment d'URL de chaque type, pour l'ajout (`/catalogue/ajouter/bd`). En
 * français : c'est une adresse que l'utilisateur voit.
 */
export const mediaTypeSlugs: Readonly<Record<MediaType, string>> = {
  Book: 'livre',
  ComicBook: 'bd',
  Music: 'musique',
  Film: 'film',
  Game: 'jeu',
};

/** Type désigné par un segment d'URL, ou `null` s'il n'en désigne aucun. */
export function mediaTypeFromSlug(slug: string | null | undefined): MediaType | null {
  return mediaTypes.find((type) => mediaTypeSlugs[type] === slug) ?? null;
}

/** Titre de l'écran d'ajout : l'article change avec le type. */
export const addTitles: Readonly<Record<MediaType, string>> = {
  Book: 'Ajouter un livre',
  ComicBook: 'Ajouter une BD',
  Music: 'Ajouter un disque',
  Film: 'Ajouter un film',
  Game: 'Ajouter un jeu',
};

/**
 * Libellé du créateur selon le type. Une seule colonne en base — le rôle
 * sémantique est le même — mais un libellé juste à l'écran : on ne cherche
 * pas le « réalisateur » d'un livre.
 */
export const creatorLabels: Readonly<Record<MediaType, string>> = {
  Book: 'Auteur',
  ComicBook: 'Scénariste',
  Music: 'Artiste',
  Film: 'Réalisateur',
  Game: 'Auteur',
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
  /**
   * Notice provisoire, décrite par l'éditeur avant catalogage : elle peut se
   * taire sur le type (une BD y passe pour un livre). Absent si faux.
   */
  readonly provisional?: boolean;
}

/**
 * Genres proposés à la saisie. Le champ reste libre, mais des libellés
 * communs évitent qu'un même genre existe en trois variantes — ce sont ceux
 * que Metadata.Api renvoie quand la source en donne un.
 */
export const bookGenres: readonly string[] = [
  'Roman',
  'Policier',
  'Thriller',
  'Science-fiction',
  'Fantasy',
  'Nouvelles',
  'Récit',
  'Conte',
  'Poésie',
  'Théâtre',
  'Essai',
  'Biographie',
  'Documentaire',
  'Album',
];

/** Film proposé par une recherche par titre. */
export interface FilmCandidate {
  /** Identifiant IMDb : `tt1375666`. */
  readonly id: string;
  readonly title: string;
  readonly year?: string | null;
}

export interface FilmSearchResult {
  readonly films: readonly FilmCandidate[];
  /** Vrai si la recherche n'a pas pu se faire : liste vide, mais pas « aucun film ». */
  readonly unavailable: boolean;
}

export interface FilmDraftResult {
  readonly draft: ScanDraft;
  readonly source: string;
}

/**
 * Fichier d'export du catalogue, relu tel quel par l'import. Ne porte ni
 * identifiant de fiche ni ménage : il s'importe chez n'importe quel ménage.
 */
export interface CatalogueExport {
  readonly format: string;
  readonly version: number;
  readonly exportedAt: string;
  readonly medias: readonly ExportedMedia[];
}

export interface ExportedMedia {
  readonly type: MediaType;
  readonly title: string;
  readonly creator?: string | null;
  readonly year?: number | null;
  readonly location?: string | null;
  readonly isLent: boolean;
  readonly addedAt?: string | null;
  readonly specifics?: MediaSpecifics | null;
}

export type DuplicateReason = 'SameIsbn' | 'SameBarcode' | 'SameTitle' | 'RepeatedInFile';

/** Fiche du fichier non importée parce qu'elle fait doublon. `position` part de 1. */
export interface SkippedMedia {
  readonly position: number;
  readonly type: MediaType;
  readonly title: string;
  readonly reason: DuplicateReason;
}

export interface ImportReport {
  readonly imported: number;
  readonly skipped: readonly SkippedMedia[];
}

export const duplicateReasonLabels: Readonly<Record<DuplicateReason, string>> = {
  SameIsbn: 'même ISBN qu’une fiche du catalogue',
  SameBarcode: 'même code-barres qu’une fiche du catalogue',
  SameTitle: 'même type, titre et auteur qu’une fiche du catalogue',
  RepeatedInFile: 'déjà plus haut dans le fichier',
};

/**
 * `Requested` : demande d'un ménage partenaire, en attente. `Active` : le
 * média est chez l'emprunteur. Les trois autres closent le prêt ou la demande.
 */
export type LoanStatus = 'Requested' | 'Active' | 'Returned' | 'Declined' | 'Cancelled';

/** Un prêt ou une demande, vu par le ménage prêteur. */
export interface Loan {
  readonly id: string;
  readonly mediaId: string;
  readonly mediaType: MediaType;
  readonly mediaTitle: string;
  readonly status: LoanStatus;
  readonly borrowerName: string;
  /** Vrai pour une demande arrivée par un partage avec un autre ménage. */
  readonly fromSharedHousehold: boolean;
  readonly requestedAt?: string | null;
  readonly lentAt?: string | null;
  readonly closedAt?: string | null;
}
