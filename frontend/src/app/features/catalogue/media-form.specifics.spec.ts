import { describe, expect, it } from 'vitest';
import {
  buildSpecifics,
  parseNumber,
  specificsToFields,
  type SpecificsFields,
} from './media-form.specifics';
import type { MediaSpecifics, MediaType } from '../../core/api/media.models';

const vide: SpecificsFields = {
  isbn: '',
  publisher: '',
  pages: null,
  series: '',
  volume: null,
  illustrator: '',
  label: '',
  format: '',
  tracks: '',
  studio: '',
  durationMinutes: null,
  support: '',
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
    });
  });

  it('rend null quand aucun champ du type ne porte de valeur', () => {
    // On n'écrit pas de document JSON vide en base.
    expect(buildSpecifics('Book', vide)).toBeNull();
    expect(buildSpecifics('Music', { ...vide, isbn: '9782070408278' })).toBeNull();
  });

  it('élague les espaces autour des valeurs', () => {
    const specificites = buildSpecifics('Book', { ...vide, publisher: '  Gallimard  ' });

    expect(specificites).toEqual({ type: 'book', isbn: null, publisher: 'Gallimard', pages: null });
  });

  it('ne confond pas un livre et une BD', () => {
    // Les deux types partagent l'ISBN et l'éditeur : seul le discriminant
    // les sépare, et une série saisie pour une BD ne doit pas se retrouver
    // dans les spécificités d'un livre.
    const livre = buildSpecifics('Book', { ...vide, isbn: '9782070408278', series: 'Blacksad' });
    expect(livre).toEqual({ type: 'book', isbn: '9782070408278', publisher: null, pages: null });

    const bd = buildSpecifics('ComicBook', {
      ...vide,
      series: 'Blacksad',
      volume: 1,
      illustrator: 'Juanjo Guarnido',
      pages: 48,
    });
    expect(bd).toEqual({
      type: 'comic',
      series: 'Blacksad',
      volume: 1,
      illustrator: 'Juanjo Guarnido',
      publisher: null,
      isbn: null,
    });
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
    { type: 'Book', specifics: { type: 'book', isbn: '9782070408278', publisher: 'Gallimard', pages: 347 } },
    {
      type: 'ComicBook',
      specifics: {
        type: 'comic',
        series: 'Blacksad',
        volume: 1,
        illustrator: 'Juanjo Guarnido',
        publisher: 'Dargaud',
        isbn: '9782205049666',
      },
    },
    {
      type: 'Music',
      specifics: {
        type: 'music',
        label: 'Columbia',
        format: 'Vinyle',
        tracks: ['So What', 'Freddie Freeloader'],
      },
    },
    { type: 'Film', specifics: { type: 'film', studio: 'Mosfilm', durationMinutes: 161, support: 'DVD' } },
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
    });
  });

  it('rend des champs vides pour des spécificités absentes', () => {
    expect(buildSpecifics('Book', specificsToFields(null))).toBeNull();
  });
});
