import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { createDetector, type CodeDetector } from './barcode-detection';
import { normalizeCode } from './scan';

/** Intervalle entre deux analyses d'image : assez vif pour la main, sobre pour la batterie. */
const scanIntervalMs = 200;

/**
 * Lecture d'un code-barres par la caméra.
 *
 * Émet **un seul** code puis coupe la caméra : on scanne un objet à la
 * fois, et laisser le flux ouvert relirait le même code en boucle.
 *
 * La caméra exige un contexte sécurisé : `localhost` en développement, HTTPS
 * partout ailleurs. Depuis un téléphone sur le réseau local en HTTP, le
 * navigateur la refuse — le champ de saisie reste alors le chemin d'entrée,
 * y compris pour une douchette USB, qui tape le code comme un clavier.
 */
@Component({
  selector: 'app-barcode-scanner',
  template: `
    <div class="viewer">
      <video #video playsinline muted aria-label="Image de la caméra"></video>
      @if (state() === 'lecture') {
        <div class="guide" aria-hidden="true"></div>
      }
    </div>

    <p class="status" role="status">
      @switch (state()) {
        @case ('demarrage') { Ouverture de la caméra… }
        @case ('lecture') { Placez le code-barres dans le cadre, bien à plat. }
        @case ('refusee') { L’accès à la caméra a été refusé. Autorisez-le dans le navigateur, ou tapez le code. }
        @case ('absente') { Aucune caméra n’est disponible. Tapez le code ci-dessus. }
        @case ('nonSecurise') { Le navigateur n’ouvre la caméra que sur une connexion sécurisée (HTTPS). Tapez le code ci-dessus. }
        @case ('echec') { La lecture n’a pas pu démarrer. Tapez le code ci-dessus. }
      }
    </p>

    <button type="button" class="discret" (click)="closed.emit()">Fermer la caméra</button>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 0.6rem;
    }

    .viewer {
      position: relative;
      background: var(--ink);
      border-radius: 2px;
      overflow: hidden;
      aspect-ratio: 4 / 3;
      max-height: 22rem;
    }

    video {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }

    /* Un cadre large et bas, à la forme d'un code-barres. */
    .guide {
      position: absolute;
      inset: 35% 12%;
      border: 2px solid var(--paper-raised);
      border-radius: 2px;
      box-shadow: 0 0 0 100vmax rgb(0 0 0 / 0.35);
    }

    .status {
      margin: 0;
      font-size: 0.875rem;
      color: var(--ink-soft);
    }

    button.discret {
      align-self: flex-start;
      background: none;
      border: 0;
      padding: 0;
      color: var(--ink-soft);
      cursor: pointer;
      text-decoration: underline;
      text-underline-offset: 0.15em;
    }
  `,
})
export class BarcodeScanner {
  /** Code lu, normalisé. Émis une seule fois. */
  readonly detected = output<string>();
  readonly closed = output<void>();

  protected readonly state = signal<
    'demarrage' | 'lecture' | 'refusee' | 'absente' | 'nonSecurise' | 'echec'
  >('demarrage');

  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');
  private stream: MediaStream | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private stopped = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stop());
    afterNextRender(() => void this.start());
  }

  private async start(): Promise<void> {
    if (!window.isSecureContext) {
      this.state.set('nonSecurise');
      return;
    }

    if (navigator.mediaDevices?.getUserMedia === undefined) {
      this.state.set('absente');
      return;
    }

    let detector: CodeDetector;
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        audio: false,
        // La caméra arrière d'un téléphone ; la seule caméra d'un portable.
        video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 } },
      });
      detector = await createDetector();
    } catch (error) {
      this.stop();
      this.state.set(this.describe(error));
      return;
    }

    // Le composant a pu être fermé pendant l'ouverture de la caméra.
    if (this.stopped) {
      this.stop();
      return;
    }

    const video = this.video().nativeElement;
    video.srcObject = this.stream;

    try {
      await video.play();
    } catch {
      this.stop();
      this.state.set('echec');
      return;
    }

    this.state.set('lecture');
    this.scan(detector, video);
  }

  private scan(detector: CodeDetector, video: HTMLVideoElement): void {
    if (this.stopped) {
      return;
    }

    this.timer = setTimeout(async () => {
      try {
        if (video.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
          const codes = await detector.detect(video);
          const code = codes.map((c) => normalizeCode(c.rawValue)).find((c) => c !== null);

          if (code !== undefined && code !== null && !this.stopped) {
            this.stop();
            this.detected.emit(code);
            return;
          }
        }
      } catch {
        // Une image illisible n'est pas une panne : on passe à la suivante.
      }

      this.scan(detector, video);
    }, scanIntervalMs);
  }

  private stop(): void {
    this.stopped = true;

    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }

    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
  }

  private describe(error: unknown): 'refusee' | 'absente' | 'echec' {
    if (error instanceof DOMException) {
      if (error.name === 'NotAllowedError' || error.name === 'SecurityError') {
        return 'refusee';
      }
      if (error.name === 'NotFoundError' || error.name === 'OverconstrainedError') {
        return 'absente';
      }
    }

    return 'echec';
  }
}
