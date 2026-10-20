import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { exportFileName, importErrors, parseExport } from './transfer';

describe('exportFileName', () => {
  it('date le fichier du jour, mois et jour sur deux chiffres', () => {
    expect(exportFileName(new Date(2026, 2, 7))).toBe('catalogue-2026-03-07.json');
  });
});

describe('parseExport', () => {
  it('accepte un export de Catalogue maison', () => {
    const parsed = parseExport(
      JSON.stringify({
        format: 'cataloguemaison',
        version: 1,
        exportedAt: '2026-10-18T10:00:00Z',
        medias: [{ type: 'Film', title: 'Alien', isLent: false }],
      }),
    );

    expect(parsed.ok).toBe(true);
    expect(parsed.ok && parsed.file.medias.length).toBe(1);
  });

  it('refuse un fichier qui n’est pas du JSON', () => {
    expect(parseExport('pas du json')).toEqual({
      ok: false,
      message: 'Ce fichier n’est pas un fichier JSON lisible.',
    });
  });

  it.each([
    ['un autre JSON', '{"items": []}'],
    ['un autre format', '{"format": "autre", "medias": []}'],
    ['une liste de médias absente', '{"format": "cataloguemaison"}'],
    ['un tableau nu', '[]'],
    ['null', 'null'],
  ])('refuse %s', (_cas, texte) => {
    expect(parseExport(texte)).toEqual({
      ok: false,
      message: 'Ce fichier n’est pas un export de Catalogue maison.',
    });
  });
});

describe('importErrors', () => {
  it('rend les messages d’un refus de validation, toutes clés confondues', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: {
        errors: {
          medias: ['Fiche 2 : Le titre est requis.', 'Fiche 3 (Dune) : spécificités d’un autre type.'],
          file: ['Version non prise en charge.'],
        },
      },
    });

    expect(importErrors(error)).toEqual([
      'Fiche 2 : Le titre est requis.',
      'Fiche 3 (Dune) : spécificités d’un autre type.',
      'Version non prise en charge.',
    ]);
  });

  it('ne rend rien pour une erreur qui n’est pas un refus de validation', () => {
    expect(importErrors(new HttpErrorResponse({ status: 403 }))).toEqual([]);
    expect(importErrors(new HttpErrorResponse({ status: 400, error: 'texte' }))).toEqual([]);
    expect(importErrors(new Error('autre'))).toEqual([]);
  });
});
