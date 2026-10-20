import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { describeSharingError } from './sharing.models';

describe('refus du partage', () => {
  it("rend l'explication d'un code d'invitation refusé", () => {
    const refus = new HttpErrorResponse({
      status: 400,
      error: { errors: { code: ["Ce code d'invitation n'est pas valable : il est erroné, expiré ou déjà utilisé."] } },
    });

    expect(describeSharingError(refus).message).toContain('expiré');
  });

  it('dit qu’un partage retiré ne se consulte plus', () => {
    const refus = new HttpErrorResponse({ status: 404, error: { type: 'https://cataloguemaison/errors/partage-introuvable' } });

    expect(describeSharingError(refus).message).toContain('plus partagé');
  });

  it('freine les essais à la chaîne', () => {
    expect(describeSharingError(new HttpErrorResponse({ status: 429 })).message).toContain('Patientez');
  });

  it('rend le détail d’un conflit, comme un média déjà prêté', () => {
    const refus = new HttpErrorResponse({ status: 409, error: { detail: 'Ce média est déjà prêté.' } });

    const probleme = describeSharingError(refus);
    expect(probleme.kind).toBe('conflit');
    expect(probleme.message).toBe('Ce média est déjà prêté.');
  });

  it('retombe sur les refus ordinaires', () => {
    expect(describeSharingError(new HttpErrorResponse({ status: 403 })).kind).toBe('interdit');
  });
});
