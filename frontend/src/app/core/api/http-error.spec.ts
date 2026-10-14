import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { describeHttpError } from './http-error';

function refus(status: number, type?: string): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    url: 'http://catalogue.test/medias',
    error: type === undefined ? null : { type, status },
  });
}

describe('describeHttpError', () => {
  it('distingue un compte sans ménage d’une session expirée', () => {
    // Les deux arrivent en 401. Confondre les deux envoie l'utilisateur se
    // reconnecter pour un problème qu'aucune reconnexion ne résout.
    const sansMenage = describeHttpError(
      refus(401, 'https://cataloguemaison/errors/tenant-absent'),
    );
    expect(sansMenage.kind).toBe('menageAbsent');
    expect(sansMenage.terminal).toBe(true);
    expect(sansMenage.message).not.toContain('expiré');

    const expiree = describeHttpError(refus(401));
    expect(expiree.kind).toBe('sessionExpiree');
    expect(expiree.terminal).toBe(false);
  });

  it('reconnaît un schéma en retard de migration', () => {
    const probleme = describeHttpError(
      refus(503, 'https://cataloguemaison/errors/schema-en-retard'),
    );

    expect(probleme.kind).toBe('schemaEnRetard');
    expect(probleme.terminal).toBe(true);
  });

  it('lit le type même si le corps arrive sous forme de texte', () => {
    const probleme = describeHttpError(
      new HttpErrorResponse({
        status: 401,
        error: JSON.stringify({ type: 'https://cataloguemaison/errors/tenant-absent' }),
      }),
    );

    expect(probleme.kind).toBe('menageAbsent');
  });

  it("ne confond pas un 503 quelconque avec un schéma en retard", () => {
    expect(describeHttpError(refus(503)).kind).toBe('autre');
  });

  it('signale une API injoignable', () => {
    expect(describeHttpError(refus(0)).kind).toBe('reseau');
  });
});
