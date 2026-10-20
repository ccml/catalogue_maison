import { describe, expect, it } from 'vitest';
import { initialsOf, isSharingSection } from './masthead';

describe('isSharingSection', () => {
  it('réunit prêts, catalogues partagés et gestion des partages', () => {
    expect(isSharingSection('/catalogue/prets')).toBe(true);
    expect(isSharingSection('/partages')).toBe(true);
    expect(isSharingSection('/partages/3f2a')).toBe(true);
    expect(isSharingSection('/menage/partage')).toBe(true);
  });

  it('laisse le catalogue et les membres hors de l’onglet', () => {
    expect(isSharingSection('/catalogue')).toBe(false);
    expect(isSharingSection('/catalogue/ajouter')).toBe(false);
    expect(isSharingSection('/catalogue/export-import')).toBe(false);
    expect(isSharingSection('/menage')).toBe(false);
  });

  it('ne se laisse pas tromper par un préfixe partiel', () => {
    expect(isSharingSection('/catalogue/pretsx')).toBe(false);
    expect(isSharingSection('/menage/partages')).toBe(false);
  });

  it('ignore la query et le fragment', () => {
    expect(isSharingSection('/partages?tri=nom')).toBe(true);
    expect(isSharingSection('/catalogue?q=prets#haut')).toBe(false);
  });
});

describe('initialsOf', () => {
  it('prend le premier et le dernier mot', () => {
    expect(initialsOf('Anne Martin')).toBe('AM');
    expect(initialsOf('Jean Pierre de la Fontaine')).toBe('JF');
  });

  it('se contente d’une lettre pour un nom d’un mot', () => {
    expect(initialsOf('bruno')).toBe('B');
  });

  it('tient les lettres accentuées', () => {
    expect(initialsOf('élise ébrard')).toBe('ÉÉ');
  });

  it('affiche un repli plutôt qu’un rond vide', () => {
    expect(initialsOf('   ')).toBe('?');
  });
});
