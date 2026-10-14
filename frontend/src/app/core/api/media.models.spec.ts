import { describe, expect, it } from 'vitest';
import { emptySpecifics, mediaTypeLabels, mediaTypes, type MediaType } from './media.models';

describe('types de média', () => {
  it('expose tous les types connus, sans doublon', () => {
    // Le défaut que ce test empêche : un nouveau type ajouté au modèle mais
    // absent de la liste d'affichage, donc invisible dans les filtres et
    // impossible à choisir à la saisie. C'est arrivé avec la BD.
    const declares = Object.keys(mediaTypeLabels) as readonly MediaType[];

    expect([...mediaTypes].sort()).toEqual([...declares].sort());
    expect(new Set(mediaTypes).size).toBe(mediaTypes.length);
  });

  it('a un libellé non vide pour chaque type', () => {
    for (const type of mediaTypes) {
      expect(mediaTypeLabels[type].length).toBeGreaterThan(0);
    }
  });

  it('sait amorcer des spécificités vides pour chaque type', () => {
    for (const type of mediaTypes) {
      expect(emptySpecifics(type).type).toBeTruthy();
    }
  });
});
