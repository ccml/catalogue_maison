import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import type { ApiProblem } from '../../core/api/http-error';
import { describeSharingError, type Share } from '../../core/api/sharing.models';
import { TenantsApi } from '../../core/api/tenants-api.service';
import { AuthService } from '../../core/auth/auth.service';

/** Les catalogues d'autres ménages que le nôtre peut consulter. */
@Component({
  selector: 'app-shared-catalogues',
  imports: [RouterLink],
  templateUrl: './shared-catalogues.html',
  styleUrl: './shared-catalogues.scss',
})
export class SharedCatalogues implements OnInit {
  private readonly api = inject(TenantsApi);

  protected readonly auth = inject(AuthService);
  protected readonly shares = signal<readonly Share[]>([]);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      this.shares.set(await firstValueFrom(this.api.receivedShares()));
    } catch (error) {
      this.problem.set(describeSharingError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
