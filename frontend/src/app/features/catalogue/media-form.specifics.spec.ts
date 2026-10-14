import { describe, expect, it } from 'vitest';
import { buildSpecifics, parseNumber, type SpecificsFields } from './media-form.specifics';

const vide: SpecificsFields = {
  isbn: '',
  publisher: '',
  pages: null,
  label: '',
  format: '',
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

  it('porte le discriminant attendu par l’API pour chaque type', () => {
    expect(buildSpecifics('Book', { ...vide, isbn: '9782070408278' })?.type).toBe('book');
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
