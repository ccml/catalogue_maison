import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { describeHouseholdError, householdIdPattern, suggestHouseholdId } from './household.models';

describe('suggestion d’identifiant de ménage', () => {
  it.each([
    ['Famille Petit', 'famille_petit'],
    ['Famille Lefèvre-Dupont', 'famille_lefevre_dupont'],
    ['  Les Œufs & Cie  ', 'les_ufs_cie'],
    ['3 frères', 'm_3_freres'],
    ['pg chez nous', 'm_pg_chez_nous'],
    ['', ''],
    ['!!!', ''],
  ])('« %s » donne « %s »', (name, expected) => {
    expect(suggestHouseholdId(name)).toBe(expected);
  });

  it('propose toujours un identifiant que l’API accepte, sans dépasser 48 caractères', () => {
    const id = suggestHouseholdId('Une famille au nom vraiment très, très, très long pour un ménage');
    expect(id.length).toBeLessThanOrEqual(48);
    expect(id).toMatch(householdIdPattern);
    expect(id.endsWith('_')).toBe(false);
  });
});

describe('refus de l’administration des ménages', () => {
  const problem = (status: number, code: string) =>
    new HttpErrorResponse({ status, error: { type: `https://cataloguemaison/errors/${code}` } });

  it('dit qu’une création interrompue se reprend', () => {
    expect(describeHouseholdError(problem(502, 'catalogue-indisponible')).message).toContain('Relancez');
  });

  it('signale un identifiant déjà pris', () => {
    expect(describeHouseholdError(problem(409, 'menage-existant')).message).toContain('déjà');
  });

  it('reprend les refus de l’administration des membres', () => {
    expect(describeHouseholdError(problem(409, 'identifiant-pris')).message).toContain('déjà utilisé');
  });

  it('détaille les erreurs de champ', () => {
    const error = new HttpErrorResponse({ status: 400, error: { errors: { id: ['Invalide.'] } } });
    expect(describeHouseholdError(error).fields).toEqual({ id: 'Invalide.' });
  });
});
