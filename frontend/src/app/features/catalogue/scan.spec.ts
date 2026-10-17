import { describe, expect, it } from 'vitest';
import type { ScanResult } from '../../core/api/media.models';
import { normalizeCode, scanNotice, sourceLabel } from './scan';

describe('normalizeCode', () => {
  it.each([
    ['978-2-205-05925-0', '9782205059250'],
    ['2 205 05925 4', '2205059254'],
    ['0-8044-2957-x', '080442957X'],
    ['724384260958', '724384260958'],
    ['96385074', '96385074'],
  ])('garde les chiffres de « %s »', (raw, expected) => {
    expect(normalizeCode(raw)).toBe(expected);
  });

  it.each(['', 'abc', '12345', '97822050592501', '97822X5059250', '9782205059250;'])(
    'refuse « %s »',
    (raw) => {
      expect(normalizeCode(raw)).toBeNull();
    },
  );
});

describe('scanNotice', () => {
  const base: ScanResult = {
    code: '9782205059250',
    existing: null,
    draft: null,
    enrichment: 'Found',
    source: 'bnf',
  };

  it('nomme la source quand la fiche est pré-remplie', () => {
    const notice = scanNotice(base);

    expect(notice.tone).toBe('info');
    expect(notice.text).toContain('Bibliothèque nationale de France');
  });

  it('signale que l’ISBN est noté quand aucune notice n’est trouvée', () => {
    const notice = scanNotice({
      ...base,
      enrichment: 'NotFound',
      source: null,
      draft: {
        type: 'Book',
        title: null,
        creator: null,
        year: null,
        specifics: { type: 'book', isbn: '9782205059250' },
      },
    });

    expect(notice.tone).toBe('attention');
    expect(notice.text).toContain('L’ISBN est déjà noté');
  });

  it('ne laisse jamais croire que l’ajout est bloqué quand l’enrichissement est en panne', () => {
    const notice = scanNotice({ ...base, enrichment: 'Unavailable', source: null });

    expect(notice.text).toContain('à la main');
  });

  it('accepte un résultat dont l’API a omis les champs nuls', () => {
    // C'est la forme réellement reçue : l'API n'écrit pas les champs nuls.
    const notice = scanNotice({ code: '9782205059250', enrichment: 'Found' });

    expect(notice.text).toContain('une source externe');
  });

  it('nomme une source inconnue par son identifiant', () => {
    expect(sourceLabel('discogs')).toBe('discogs');
    expect(sourceLabel(null)).toBe('une source externe');
  });
});
