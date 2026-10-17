import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { describeMemberError } from './member.models';

function problem(status: number, body: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: body });
}

describe('describeMemberError', () => {
  it('rattache les erreurs de validation à leurs champs', () => {
    const described = describeMemberError(
      problem(400, { errors: { username: ['Trop court.'], temporaryPassword: ['Au moins 8.'] } }),
    );

    expect(described.fields).toEqual({ username: 'Trop court.', temporaryPassword: 'Au moins 8.' });
  });

  it('dit de rejouer une opération restée à mi-chemin', () => {
    const described = describeMemberError(
      problem(503, { type: 'https://cataloguemaison/errors/registre-indisponible' }),
    );

    expect(described.message).toContain('Relancez');
  });

  it("reprend le motif d'un refus métier formulé par l'API", () => {
    const described = describeMemberError(
      problem(409, {
        type: 'https://cataloguemaison/errors/soi-meme',
        detail: 'Vous ne pouvez pas désactiver votre propre compte.',
      }),
    );

    expect(described.message).toBe('Vous ne pouvez pas désactiver votre propre compte.');
  });

  it('lit un corps resté sous forme de texte', () => {
    const described = describeMemberError(
      problem(409, JSON.stringify({ type: 'https://cataloguemaison/errors/identifiant-pris' })),
    );

    expect(described.message).toContain('déjà utilisé');
  });

  it('distingue un service injoignable', () => {
    expect(describeMemberError(problem(0, null)).message).toContain('injoignable');
  });
});
