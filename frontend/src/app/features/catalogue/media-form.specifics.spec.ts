import { describe, expect, it } from 'vitest';
import {
  buildSpecifics,
  lineNumbers,
  parseLines,
  parseNumber,
  specificsToFields,
  type SpecificsFields,
} from './media-form.specifics';
import type { MediaSpecifics, MediaType } from '../../core/api/media.models';

const vide: SpecificsFields = {
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

describe('buildSpecifics', () => {
  it("ne retient que les champs du type demandé", () => {
    // Le cas qui justifie cette fonction : l'utilisateur a saisi un ISBN,
    // puis changé le type pour « Film ». L'ISBN ne doit pas partir.
    const specificites = buildSpecifics('Film', {
      ...vide,
      isbn: '9782070408278',
      studio: 'Legendary',
    });

    expect(specificites).toEqual({
      type: 'film',
      studio: 'Legendary',
      durationMinutes: null,
      support: null,
      genre: null,
      actors: [],
    });
  });

  it('rend null quand aucun champ du type ne porte de valeur', () => {
    // On n'écrit pas de document JSON vide en base.
    expect(buildSpecifics('Book', vide)).toBeNull();
    expect(buildSpecifics('Music', { ...vide, isbn: '9782070408278' })).toBeNull();
  });

  it('élague les espaces autour des valeurs', () => {
    const specificites = buildSpecifics('Book', { ...vide, publisher: '  Gallimard  ' });

    expect(specificites).toEqual({
      type: 'book',
      isbn: null,
      publisher: 'Gallimard',
      pages: null,
      series: null, volume: null, collection: null, collectionNumber: null, genre: null,
    });
  });

  it('ne confond pas un livre et une BD', () => {
    // Série, tome et collection sont communs aux deux : un album pris pour
    // un livre les garde en changeant de type. Mais ce qui n'appartient qu'à
    // l'un — le dessinateur et l'édition d'une BD, le genre d'un livre — ne
    // doit pas passer à l'autre.
    const champs: SpecificsFields = {
      ...vide,
      isbn: '9782070408278',
      series: 'Blacksad',
      volume: 1,
      illustrator: 'Juanjo Guarnido',
      collection: 'Folio',
      collectionNumber: '85',
      genre: 'Policier',
      edition: 'Éd. de luxe',
    };

    expect(buildSpecifics('Book', champs)).toEqual({
      type: 'book',
      isbn: '9782070408278',
      publisher: null,
      pages: null,
      series: 'Blacksad',
      volume: 1,
      collection: 'Folio',
      collectionNumber: '85',
      genre: 'Policier',
    });

    expect(buildSpecifics('ComicBook', champs)).toEqual({
      type: 'comic',
      series: 'Blacksad',
      volume: 1,
      illustrator: 'Juanjo Guarnido',
      publisher: null,
      isbn: '9782070408278',
      collection: 'Folio',
      collectionNumber: '85',
      edition: 'Éd. de luxe',
    });
  });

  it('une mention d’édition seule suffit à écrire les spécificités d’une BD', () => {
    expect(buildSpecifics('ComicBook', { ...vide, edition: 'Intégrale' })?.type).toBe('comic');
  });

  it('une collection seule suffit à écrire les spécificités d’un livre', () => {
    expect(buildSpecifics('Book', { ...vide, collection: 'Pléiade' })?.type).toBe('book');
  });

  it('porte le discriminant attendu par l’API pour chaque type', () => {
    expect(buildSpecifics('Book', { ...vide, isbn: '9782070408278' })?.type).toBe('book');
    expect(buildSpecifics('ComicBook', { ...vide, series: 'Astérix' })?.type).toBe('comic');
    expect(buildSpecifics('Music', { ...vide, label: 'Columbia' })?.type).toBe('music');
    expect(buildSpecifics('Film', { ...vide, studio: 'Mosfilm' })?.type).toBe('film');
    expect(buildSpecifics('Game', { ...vide, platform: 'Plateau' })?.type).toBe('game');
  });
});

describe('parseNumber', () => {
  it('lit un nombre saisi au clavier', () => {
    expect(parseNumber('347')).toBe(347);
    expect(parseNumber('  1968 ')).toBe(1968);
  });

  it('rend null pour une saisie vide ou inexploitable', () => {
    expect(parseNumber('')).toBeNull();
    expect(parseNumber('   ')).toBeNull();
    expect(parseNumber('mille')).toBeNull();
  });
});

describe('specificsToFields', () => {
  /**
   * L'invariant qui protège les corrections de fiche : un champ présent en
   * base mais absent du formulaire serait effacé silencieusement à la
   * première modification. L'aller-retour le prouve type par type.
   */
  const cas: readonly { readonly type: MediaType; readonly specifics: MediaSpecifics }[] = [
    {
      type: 'Book',
      specifics: {
        type: 'book',
        isbn: '9782070541300',
        publisher: 'Gallimard',
        pages: 349,
        series: 'Harry Potter',
        volume: 3,
        collection: 'Folio junior',
        collectionNumber: '1006',
        genre: 'Roman',
      },
    },
    {
      type: 'ComicBook',
      specifics: {
        type: 'comic',
        series: 'Blacksad',
        volume: 1,
        illustrator: 'Juanjo Guarnido',
        publisher: 'Dargaud',
        isbn: '9782205049666',
        collection: 'Aire libre',
        collectionNumber: '12',
        edition: 'Éd. de luxe',
      },
    },
    {
      type: 'Music',
      specifics: {
        type: 'music',
        label: 'Columbia',
        format: 'Vinyle',
        tracks: ['So What', 'Freddie Freeloader'],
        barcode: '5099702798320',
      },
    },
    {
      type: 'Film',
      specifics: {
        type: 'film',
        studio: 'Mosfilm',
        durationMinutes: 161,
        support: 'DVD',
        genre: 'Drama, Sci-Fi',
        actors: ['Natalya Bondarchuk', 'Donatas Banionis'],
      },
    },
    { type: 'Game', specifics: { type: 'game', platform: 'Plateau', minPlayers: 2, maxPlayers: 5 } },
  ];

  for (const { type, specifics } of cas) {
    it(`restitue sans perte les spécificités de type ${type}`, () => {
      expect(buildSpecifics(type, specificsToFields(specifics))).toEqual(specifics);
    });
  }

  it('ne perd pas les pistes d’un album lors d’une correction', () => {
    // Ce champ n'était pas capturé par le formulaire : corriger une fiche
    // musique effaçait ses pistes sans rien signaler.
    const champs = specificsToFields({ type: 'music', label: 'ECM', tracks: ['Rubato', 'Coda'] });

    expect(champs.tracks).toBe('Rubato\nCoda');
    expect(buildSpecifics('Music', champs)).toEqual({
      type: 'music',
      label: 'ECM',
      format: null,
      tracks: ['Rubato', 'Coda'],
      barcode: null,
    });
  });

  it('ne perd pas le code-barres d’un disque lors d’une correction', () => {
    // Le perdre ferait passer le disque pour absent au prochain scan, et
    // l'utilisateur l'ajouterait une seconde fois.
    const champs = specificsToFields({ type: 'music', barcode: '0724384260958' });

    expect(champs.barcode).toBe('0724384260958');
    expect(buildSpecifics('Music', champs)).toMatchObject({ barcode: '0724384260958' });
  });

  it('un code-barres seul suffit à écrire les spécificités d’un disque', () => {
    expect(buildSpecifics('Music', { ...vide, barcode: '0724384260958' })).toEqual({
      type: 'music',
      label: null,
      format: null,
      tracks: [],
      barcode: '0724384260958',
    });
  });

  it('rend des champs vides pour des spécificités absentes', () => {
    expect(buildSpecifics('Book', specificsToFields(null))).toBeNull();
  });
});

describe('lineNumbers', () => {
  it('numérote les pistes dans l’ordre de saisie', () => {
    expect(lineNumbers('So What\nFreddie Freeloader\nBlue in Green')).toEqual([1, 2, 3]);
  });

  it('ne numérote pas les lignes vides, ignorées à l’enregistrement', () => {
    // Le numéro affiché doit être celui de la piste enregistrée : une ligne
    // vide au milieu ne décale pas la suite.
    expect(lineNumbers('Rubato\n\n  \nCoda\n')).toEqual([1, null, null, 2, null]);
    expect(parseLines('Rubato\n\n  \nCoda\n')).toEqual(['Rubato', 'Coda']);
  });

  it('rend une seule ligne sans numéro pour un champ vide', () => {
    expect(lineNumbers('')).toEqual([null]);
  });
});
