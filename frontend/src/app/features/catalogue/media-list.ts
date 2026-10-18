import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, distinctUntilChanged, firstValueFrom } from 'rxjs';
import { CatalogueApi } from '../../core/api/catalogue-api.service';
import { describeHttpError, type ApiProblem } from '../../core/api/http-error';
import {
  mediaSortLabels,
  mediaSorts,
  mediaTypeLabels,
  mediaTypes,
  type MediaQuery,
  type MediaSort,
  type MediaSummary,
  type MediaType,
} from '../../core/api/media.models';
import { AuthService } from '../../core/auth/auth.service';
import { Icon, mediaTypeIcons } from '../../shared/icon';

/** Médias chargés à la fois : la liste s'allonge à la demande. */
const pageSize = 50;

@Component({
  selector: 'app-media-list',
  imports: [RouterLink, Icon],
  templateUrl: './media-list.html',
  styleUrl: './media-list.scss',
})
export class MediaList implements OnInit {
  private readonly api = inject(CatalogueApi);
  private readonly typed = new Subject<string>();

  protected readonly auth = inject(AuthService);

  protected readonly term = signal('');
  protected readonly type = signal<MediaType | null>(null);
  protected readonly sort = signal<MediaSort>('Recent');
  protected readonly items = signal<readonly MediaSummary[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly loadingMore = signal(false);

  /** Ce qui reste à afficher au-delà des médias déjà chargés. */
  protected readonly remaining = computed(() => this.total() - this.items().length);
  protected readonly nextCount = computed(() => Math.min(pageSize, this.remaining()));

  /** Dernière page chargée, et génération de la liste à laquelle elle appartient. */
  private page = 1;
  private generation = 0;
  protected readonly probleme = signal<ApiProblem | null>(null);

  /**
   * Refus qu'aucune reconnexion ne résoudra : un compte sans ménage, un
   * schéma en retard. L'écran doit alors dire ce qui se passe, et non
   * afficher un catalogue vide sous un bandeau d'erreur.
   */
  protected readonly refusDefinitif = computed(() => {
    const probleme = this.probleme();
    return probleme !== null && probleme.terminal ? probleme : null;
  });

  protected readonly types = mediaTypes;
  protected readonly sorts = mediaSorts;
  protected readonly sortLabels = mediaSortLabels;
  protected readonly labels = mediaTypeLabels;
  protected readonly icons = mediaTypeIcons;

  constructor() {
    // Une frappe ne déclenche pas une requête : on attend la fin du mot.
    this.typed
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((value) => {
        this.term.set(value);
        void this.load();
      });
  }

  ngOnInit(): void {
    void this.load();
  }

  /** « Auteur, année », en omettant ce qui manque. */
  protected attribution(media: MediaSummary): string {
    const parts: string[] = [];

    if (media.creator) {
      parts.push(media.creator);
    }
    if (media.year != null) {
      parts.push(String(media.year));
    }

    return parts.join(', ');
  }

  protected search(value: string): void {
    this.typed.next(value);
  }

  protected filterByType(type: MediaType | null): void {
    this.type.set(type);
    void this.load();
  }

  protected sortBy(value: string): void {
    const sort = mediaSorts.find((candidate) => candidate === value);
    if (sort === undefined) {
      return;
    }
    this.sort.set(sort);
    void this.load();
  }

  protected async showMore(): Promise<void> {
    const generation = this.generation;
    this.loadingMore.set(true);

    try {
      const page = await firstValueFrom(this.api.list(this.query(this.page + 1)));
      // Un filtre changé entre-temps a reconstruit la liste : cette page
      // appartient à l'ancienne.
      if (generation !== this.generation) {
        return;
      }
      this.page = page.page;
      this.items.update((items) => [...items, ...page.items]);
      this.total.set(page.total);
    } catch (error) {
      this.probleme.set(describeHttpError(error));
    } finally {
      this.loadingMore.set(false);
    }
  }

  protected async toggleLent(media: MediaSummary): Promise<void> {
    const wanted = !media.isLent;

    try {
      await firstValueFrom(this.api.setLent(media.id, wanted));
      this.items.update((items) =>
        items.map((item) => (item.id === media.id ? { ...item, isLent: wanted } : item)),
      );
    } catch (error) {
      this.probleme.set(describeHttpError(error));
    }
  }

  /** Reconstruit la liste depuis sa première page. */
  private async load(): Promise<void> {
    const generation = ++this.generation;
    this.loading.set(true);
    this.loadingMore.set(false);
    this.probleme.set(null);

    try {
      const page = await firstValueFrom(this.api.list(this.query(1)));
      // Une frappe plus récente a relancé la lecture : sa réponse fait foi.
      if (generation !== this.generation) {
        return;
      }
      this.page = 1;
      this.items.set(page.items);
      this.total.set(page.total);
    } catch (error) {
      if (generation !== this.generation) {
        return;
      }
      this.items.set([]);
      this.total.set(0);
      this.probleme.set(describeHttpError(error));
    } finally {
      if (generation === this.generation) {
        this.loading.set(false);
      }
    }
  }

  private query(page: number): MediaQuery {
    return { q: this.term(), type: this.type(), sort: this.sort(), page, size: pageSize };
  }
}
