import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { MediaType } from '../core/api/media.models';

/**
 * Jeu d'icônes d'interface, repris du canevas « Icônes CatalogueMaison »
 * (Claude Design) : grille 24, trait 1,75, extrémités et angles arrondis.
 *
 * Les dessins sont des données et non du SVG brut : pas d'`innerHTML`, donc
 * rien à faire passer par le sanitizer. Le trait suit `currentColor` : une
 * icône prend la couleur du texte qu'elle accompagne, en clair comme en
 * sombre.
 */
type Shape =
  | { readonly path: string }
  | { readonly circle: readonly [cx: number, cy: number, r: number]; readonly solid?: true }
  | { readonly rect: readonly [x: number, y: number, w: number, h: number, rx: number] };

const drawings = {
  book: [{ path: 'M6 4h10a2 2 0 0 1 2 2v14H8a2 2 0 0 1-2-2V4Z' }, { path: 'M18 18H8a2 2 0 0 0-2 2' }],
  music: [
    { circle: [12, 12, 8.5] },
    { circle: [12, 12, 3] },
    { circle: [12, 12, 0.6], solid: true },
  ],
  film: [{ rect: [3, 5, 18, 14, 2] }, { path: 'M7.5 5v14' }, { path: 'M16.5 5v14' }],
  series: [{ rect: [3, 8, 18, 12, 2] }, { path: 'M8 3.5 12 8l4-4.5' }],
  comic: [{ path: 'M5 4h14a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1h-8l-4 4v-4H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z' }],
  game: [
    { rect: [2.5, 7, 19, 10, 4] },
    { path: 'M7 10v4' },
    { path: 'M5 12h4' },
    { circle: [15.5, 11, 1], solid: true },
    { circle: [18, 13.5, 1], solid: true },
  ],
  home: [{ path: 'M3 11 12 4l9 7' }, { path: 'M5.5 9.5V20h13V9.5' }],
  search: [{ circle: [11, 11, 6.5] }, { path: 'M20 20l-4.4-4.4' }],
  scan: [
    { path: 'M3 8V5a1 1 0 0 1 1-1h3' },
    { path: 'M17 4h3a1 1 0 0 1 1 1v3' },
    { path: 'M21 16v3a1 1 0 0 1-1 1h-3' },
    { path: 'M7 20H4a1 1 0 0 1-1-1v-3' },
    { path: 'M8 8.5v7' },
    { path: 'M12 8.5v7' },
    { path: 'M16 8.5v7' },
  ],
  add: [{ circle: [12, 12, 8.5] }, { path: 'M12 8v8' }, { path: 'M8 12h8' }],
  collection: [
    { path: 'M5 5v11' },
    { path: 'M9.5 5v11' },
    { path: 'M14 5v11' },
    { path: 'M17.5 6l2.5 10' },
    { path: 'M4 19h16' },
  ],
  genre: [{ path: 'M20.6 12.6 12 21.2l-8.5-8.5V4.5a1 1 0 0 1 1-1h8.2l7.9 7.9a1 1 0 0 1 0 1.2Z' }, { circle: [8, 8, 1.3] }],
  lent: [
    { path: 'M13 4H5a1 1 0 0 0-1 1v14a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-8' },
    { path: 'M14 10 20.5 3.5' },
    { path: 'M15 3.5h5.5V9' },
  ],
  favorite: [{ path: 'M12 3.6l2.6 5.5 5.9.8-4.3 4.2 1 6-5.2-2.9-5.2 2.9 1-6L3.5 9.9l5.9-.8Z' }],
  review: [{ path: 'M4 20l.8-3.6L15.4 5.8a2 2 0 0 1 2.8 2.8L7.6 19.2 4 20Z' }, { path: 'M14.4 6.8l2.8 2.8' }],
  filter: [{ path: 'M4 5h16l-6 7v6.5l-4 2V12L4 5Z' }],
  sort: [{ path: 'M7 4v16' }, { path: 'M4 17l3 3 3-3' }, { path: 'M17 20V4' }, { path: 'M14 7l3-3 3 3' }],
  grid: [
    { rect: [4, 4, 7, 7, 1.5] },
    { rect: [13, 4, 7, 7, 1.5] },
    { rect: [4, 13, 7, 7, 1.5] },
    { rect: [13, 13, 7, 7, 1.5] },
  ],
  list: [
    { path: 'M9 6h11' },
    { path: 'M9 12h11' },
    { path: 'M9 18h11' },
    { circle: [4.5, 6, 1.1], solid: true },
    { circle: [4.5, 12, 1.1], solid: true },
    { circle: [4.5, 18, 1.1], solid: true },
  ],
  storage: [
    { rect: [3, 5, 18, 4.5, 1.2] },
    { path: 'M5 9.5V19a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9.5' },
    { path: 'M10 14h4' },
  ],
  stats: [{ path: 'M4 20h16' }, { path: 'M7 20v-5.5' }, { path: 'M12 20V8' }, { path: 'M17 20v-9' }],
  import: [
    { path: 'M12 3.5v10' },
    { path: 'M8 10l4 4 4-4' },
    { path: 'M4 16.5V19a1.5 1.5 0 0 0 1.5 1.5h13A1.5 1.5 0 0 0 20 19v-2.5' },
  ],
  settings: [
    { path: 'M4 7h9' },
    { path: 'M19 7h1' },
    { path: 'M4 12h4' },
    { path: 'M14 12h6' },
    { path: 'M4 17h9' },
    { path: 'M19 17h1' },
    { circle: [16, 7, 2.2] },
    { circle: [11, 12, 2.2] },
    { circle: [16, 17, 2.2] },
  ],
  delete: [
    { path: 'M4 7h16' },
    { path: 'M9 7V5a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2' },
    { path: 'M6.5 7l.8 12.1a1 1 0 0 0 1 .9h7.4a1 1 0 0 0 1-.9L17.5 7' },
    { path: 'M10 11v6' },
    { path: 'M14 11v6' },
  ],
} as const satisfies Record<string, readonly Shape[]>;

export type IconName = keyof typeof drawings;

/**
 * Pictogramme de chaque type. **Source unique**, comme `mediaTypeLabels` :
 * un type ajouté sans icône ne compile pas.
 */
export const mediaTypeIcons: Readonly<Record<MediaType, IconName>> = {
  Book: 'book',
  ComicBook: 'comic',
  Music: 'music',
  Film: 'film',
  Game: 'game',
};

/**
 * Une icône du jeu. Décorative par défaut (`aria-hidden`) : elle accompagne un
 * libellé. Seule, elle doit recevoir un `label`, lu comme une image.
 */
@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.role]': 'label() ? "img" : null',
    '[attr.aria-label]': 'label()',
    '[attr.aria-hidden]': 'label() ? null : "true"',
    '[style.--icon-size]': 'size()',
  },
  template: `
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.75"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      @for (shape of shapes(); track $index) {
        @if ('path' in shape) {
          <svg:path [attr.d]="shape.path" />
        } @else if ('circle' in shape) {
          <svg:circle
            [attr.cx]="shape.circle[0]"
            [attr.cy]="shape.circle[1]"
            [attr.r]="shape.circle[2]"
            [attr.fill]="shape.solid ? 'currentColor' : null"
          />
        } @else {
          <svg:rect
            [attr.x]="shape.rect[0]"
            [attr.y]="shape.rect[1]"
            [attr.width]="shape.rect[2]"
            [attr.height]="shape.rect[3]"
            [attr.rx]="shape.rect[4]"
          />
        }
      }
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
      width: var(--icon-size);
      height: var(--icon-size);
      /* Aligné sur l'œil du texte quand l'icône est posée en ligne. */
      vertical-align: -0.2em;
    }

    svg {
      width: 100%;
      height: 100%;
    }
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  /** Taille CSS ; par défaut, celle du texte environnant. */
  readonly size = input('1.25em');
  /** Nom accessible, pour une icône sans libellé visible. */
  readonly label = input<string | null>(null);

  protected readonly shapes = computed<readonly Shape[]>(() => drawings[this.name()]);
}
