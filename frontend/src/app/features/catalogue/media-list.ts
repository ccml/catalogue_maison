import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, distinctUntilChanged, firstValueFrom } from 'rxjs';
import { CatalogueApi } from '../../core/api/catalogue-api.service';
import { describeHttpError } from '../../core/api/http-error';
import { mediaTypeLabels, type MediaSummary, type MediaType } from '../../core/api/media.models';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-media-list',
  imports: [RouterLink],
  templateUrl: './media-list.html',
  styleUrl: './media-list.scss',
})
export class MediaList implements OnInit {
  private readonly api = inject(CatalogueApi);
  private readonly typed = new Subject<string>();

  protected readonly auth = inject(AuthService);

  protected readonly term = signal('');
  protected readonly type = signal<MediaType | null>(null);
  protected readonly items = signal<readonly MediaSummary[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly types: readonly MediaType[] = ['Book', 'Music', 'Film', 'Game'];
  protected readonly labels = mediaTypeLabels;

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

    if (media.creator !== null) {
      parts.push(media.creator);
    }
    if (media.year !== null) {
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

  protected async toggleLent(media: MediaSummary): Promise<void> {
    const wanted = !media.isLent;

    try {
      await firstValueFrom(this.api.setLent(media.id, wanted));
      this.items.update((items) =>
        items.map((item) => (item.id === media.id ? { ...item, isLent: wanted } : item)),
      );
    } catch (error) {
      this.error.set(describeHttpError(error));
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const page = await firstValueFrom(
        this.api.list({ q: this.term(), type: this.type(), size: 100 }),
      );
      this.items.set(page.items);
      this.total.set(page.total);
    } catch (error) {
      this.items.set([]);
      this.total.set(0);
      this.error.set(describeHttpError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
