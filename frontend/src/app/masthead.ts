/* Règles d'affichage de l'en-tête, isolées pour être testées sans gabarit. */

/**
 * Pages réunies sous l'onglet « Partage » : les prêts, les catalogues
 * partagés avec nous, et la gestion de nos propres partages. Elles vivent
 * sous trois préfixes d'URL différents, d'où cette liste plutôt qu'un
 * `routerLinkActive`.
 */
const SHARING_PATHS = ['/catalogue/prets', '/partages', '/menage/partage'] as const;

export function isSharingSection(url: string): boolean {
  const path = url.split(/[?#]/, 1)[0];
  return SHARING_PATHS.some((prefix) => path === prefix || path.startsWith(`${prefix}/`));
}

/** Deux lettres au plus, celles du premier et du dernier mot du nom affiché. */
export function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/).filter((w) => w.length > 0);
  if (words.length === 0) {
    return '?';
  }
  const first = [...words[0]][0];
  const last = words.length > 1 ? [...words[words.length - 1]][0] : '';
  return (first + last).toLocaleUpperCase('fr');
}
