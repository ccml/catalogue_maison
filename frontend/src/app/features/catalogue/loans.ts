import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom, type Observable } from 'rxjs';
import { CatalogueApi } from '../../core/api/catalogue-api.service';
import { describeHttpError, type ApiProblem } from '../../core/api/http-error';
import { mediaTypeLabels, type Loan } from '../../core/api/media.models';
import { AuthService } from '../../core/auth/auth.service';
import { Icon, mediaTypeIcons } from '../../shared/icon';

/**
 * Les prêts du ménage : demandes reçues des ménages partenaires, puis prêts
 * en cours. Tout membre les voit ; répondre est réservé à qui écrit au
 * catalogue — l'API le revalide, les boutons ne sont qu'un confort.
 */
@Component({
  selector: 'app-loans',
  imports: [Icon],
  templateUrl: './loans.html',
  styleUrl: './loans.scss',
})
export class Loans implements OnInit {
  private readonly api = inject(CatalogueApi);

  protected readonly auth = inject(AuthService);
  protected readonly labels = mediaTypeLabels;
  protected readonly icons = mediaTypeIcons;

  protected readonly loans = signal<readonly Loan[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);

  protected readonly requests = computed(() => this.loans().filter((loan) => loan.status === 'Requested'));
  protected readonly active = computed(() => this.loans().filter((loan) => loan.status === 'Active'));

  ngOnInit(): void {
    void this.load();
  }

  protected accept(loan: Loan): Promise<void> {
    return this.act(loan, this.api.acceptLoan(loan.id));
  }

  protected decline(loan: Loan): Promise<void> {
    return this.act(loan, this.api.declineLoan(loan.id));
  }

  protected markReturned(loan: Loan): Promise<void> {
    return this.act(loan, this.api.returnLoan(loan.id));
  }

  protected since(value: string | null | undefined): string {
    return value ? new Date(value).toLocaleDateString('fr-FR', { day: 'numeric', month: 'long' }) : '';
  }

  /**
   * Après chaque réponse, la liste est relue plutôt que corrigée sur place :
   * accepter une demande en refuse d'autres, que seule l'API connaît.
   */
  private async act(loan: Loan, call: Observable<void>): Promise<void> {
    this.busy.set(loan.id);
    this.problem.set(null);

    try {
      await firstValueFrom(call);
    } catch (error) {
      this.problem.set(describeHttpError(error));
    } finally {
      this.busy.set(null);
    }

    await this.load();
  }

  private async load(): Promise<void> {
    try {
      this.loans.set(await firstValueFrom(this.api.loans()));
    } catch (error) {
      this.problem.set(describeHttpError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
