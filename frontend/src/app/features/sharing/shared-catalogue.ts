import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, firstValueFrom } from 'rxjs';
import type { ApiProblem } from '../../core/api/http-error';
import { mediaTypeLabels, mediaTypes, type MediaType } from '../../core/api/media.models';
import {
  describeSharingError,
  sharedAvailabilityLabels,
  sharedLoanStatusLabels,
  type SharedLoan,
  type SharedMedia,
} from '../../core/api/sharing.models';
import { TenantsApi } from '../../core/api/tenants-api.service';
import { AuthService } from '../../core/auth/auth.service';
import { Icon, mediaTypeIcons } from '../../shared/icon';

const pageSize = 50;

/**
 * Le catalogue d'un autre ménage, en lecture seule. Tout membre le consulte ;
 * adultes et administrateur y demandent un prêt. L'API revérifie le partage
 * et le rôle à chaque appel : les boutons ne sont qu'un confort.
 */
@Component({
  selector: 'app-shared-catalogue',
  imports: [RouterLink, Icon],
  templateUrl: './shared-catalogue.html',
  styleUrls: ['../catalogue/media-list.scss', './shared-catalogue.scss'],
})
export class SharedCatalogue implements OnInit {
  private readonly api = inject(TenantsApi);
  private readonly typed = new Subject<string>();

  /** Identifiant du partage, lu dans l'URL. */
  readonly shareId = input.required<string>();

  protected readonly auth = inject(AuthService);
  protected readonly householdName = signal<string | null>(null);
  protected readonly term = signal('');
  protected readonly type = signal<MediaType | null>(null);
  protected readonly items = signal<readonly SharedMedia[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly loans = signal<readonly SharedLoan[]>([]);

  protected readonly remaining = computed(() => this.total() - this.items().length);
  protected readonly openLoans = computed(() =>
    this.loans().filter((loan) => loan.status === 'Requested' || loan.status === 'Active'),
  );

  protected readonly types = mediaTypes;
  protected readonly labels = mediaTypeLabels;
  protected readonly icons = mediaTypeIcons;
  protected readonly availabilityLabels = sharedAvailabilityLabels;
  protected readonly loanLabels = sharedLoanStatusLabels;

  private page = 1;
  private generation = 0;

  constructor() {
    this.typed
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((value) => {
        this.term.set(value);
        void this.load();
      });
  }

  async ngOnInit(): Promise<void> {
    try {
      const shares = await firstValueFrom(this.api.receivedShares());
      this.householdName.set(shares.find((share) => share.id === this.shareId())?.householdName ?? null);
    } catch (error) {
      this.problem.set(describeSharingError(error));
    }

    await Promise.all([this.load(), this.loadLoans()]);
  }

  protected attribution(media: SharedMedia): string {
    const parts: string[] = [];
    if (media.series) {
      parts.push(media.volume != null ? `${media.series}, t. ${media.volume}` : media.series);
    }
    if (media.creator) {
      parts.push(media.creator);
    }
    if (media.year != null) {
      parts.push(String(media.year));
    }
    return parts.join(' · ');
  }

  protected search(value: string): void {
    this.typed.next(value);
  }

  protected filterByType(type: MediaType | null): void {
    this.type.set(type);
    void this.load();
  }

  protected async request(media: SharedMedia): Promise<void> {
    this.busy.set(media.id);
    this.problem.set(null);
    try {
      await firstValueFrom(this.api.requestLoan(this.shareId(), media.id));
      this.replace(media.id, 'RequestedByYou');
    } catch (error) {
      this.problem.set(describeSharingError(error));
    } finally {
      this.busy.set(null);
    }
    await this.loadLoans();
  }

  protected async cancel(loan: SharedLoan): Promise<void> {
    this.busy.set(loan.id);
    this.problem.set(null);
    try {
      await firstValueFrom(this.api.cancelLoanRequest(this.shareId(), loan.id));
      this.replace(loan.mediaId, 'Available');
    } catch (error) {
      this.problem.set(describeSharingError(error));
    } finally {
      this.busy.set(null);
    }
    await this.loadLoans();
  }

  protected async showMore(): Promise<void> {
    const generation = this.generation;
    this.loadingMore.set(true);
    try {
      const page = await firstValueFrom(
        this.api.sharedMedias(this.shareId(), {
          q: this.term(),
          type: this.type(),
          page: this.page + 1,
          size: pageSize,
        }),
      );
      if (generation !== this.generation) {
        return;
      }
      this.page = page.page;
      this.items.update((items) => [...items, ...page.items]);
      this.total.set(page.total);
    } catch (error) {
      this.problem.set(describeSharingError(error));
    } finally {
      this.loadingMore.set(false);
    }
  }

  private replace(mediaId: string, availability: SharedMedia['availability']): void {
    this.items.update((items) => items.map((item) => (item.id === mediaId ? { ...item, availability } : item)));
  }

  private async load(): Promise<void> {
    const generation = ++this.generation;
    this.loading.set(true);

    try {
      const page = await firstValueFrom(
        this.api.sharedMedias(this.shareId(), { q: this.term(), type: this.type(), page: 1, size: pageSize }),
      );
      if (generation !== this.generation) {
        return;
      }
      this.page = 1;
      this.items.set(page.items);
      this.total.set(page.total);
    } catch (error) {
      if (generation === this.generation) {
        this.items.set([]);
        this.total.set(0);
        this.problem.set(describeSharingError(error));
      }
    } finally {
      if (generation === this.generation) {
        this.loading.set(false);
      }
    }
  }

  /** Les demandes ne concernent que qui peut en faire : adultes et administrateur. */
  private async loadLoans(): Promise<void> {
    if (!this.auth.mayWrite()) {
      return;
    }
    try {
      this.loans.set(await firstValueFrom(this.api.sharedLoans(this.shareId())));
    } catch {
      // La liste des demandes est un complément : son échec ne masque pas le catalogue.
      this.loans.set([]);
    }
  }
}
