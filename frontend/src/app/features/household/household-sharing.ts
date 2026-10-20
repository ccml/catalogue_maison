import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom, type Observable } from 'rxjs';
import type { ApiProblem } from '../../core/api/http-error';
import {
  describeSharingError,
  type CreatedInvitation,
  type Invitation,
  type Share,
} from '../../core/api/sharing.models';
import { TenantsApi } from '../../core/api/tenants-api.service';

/**
 * Partage du catalogue, pour l'administrateur du ménage : inviter un autre
 * ménage à consulter le nôtre, accepter l'invitation d'un autre, et retirer
 * ce qui ne doit plus l'être. Tenants.Api revérifie le rôle à chaque appel.
 */
@Component({
  selector: 'app-household-sharing',
  imports: [RouterLink],
  templateUrl: './household-sharing.html',
  styleUrl: './household-sharing.scss',
})
export class HouseholdSharing implements OnInit {
  private readonly api = inject(TenantsApi);

  protected readonly invitations = signal<readonly Invitation[]>([]);
  protected readonly granted = signal<readonly Share[]>([]);
  protected readonly received = signal<readonly Share[]>([]);
  protected readonly created = signal<CreatedInvitation | null>(null);
  protected readonly copied = signal(false);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly confirmation = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  protected async invite(): Promise<void> {
    await this.run(this.api.createInvitation(), (invitation) => {
      this.created.set(invitation);
      this.copied.set(false);
    });
  }

  protected async copy(code: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(code);
      this.copied.set(true);
    } catch {
      // Presse-papiers refusé (contexte non sécurisé) : le code reste
      // affiché, il se recopie à la main.
      this.copied.set(false);
    }
  }

  protected async cancelInvitation(invitation: Invitation): Promise<void> {
    await this.run(this.api.cancelInvitation(invitation.id));
  }

  protected async accept(code: string): Promise<void> {
    if (code.trim().length === 0) {
      return;
    }
    await this.run(this.api.acceptInvitation(code), (share) =>
      this.confirmation.set(`Le catalogue de ${share.householdName} est maintenant partagé avec votre ménage.`),
    );
  }

  protected async revoke(share: Share): Promise<void> {
    if (!window.confirm(`${share.householdName} ne pourra plus consulter votre catalogue. Continuer ?`)) {
      return;
    }
    await this.run(this.api.revokeShare(share.id));
  }

  protected async renounce(share: Share): Promise<void> {
    if (!window.confirm(`Votre ménage ne verra plus le catalogue de ${share.householdName}. Continuer ?`)) {
      return;
    }
    await this.run(this.api.renounceShare(share.id));
  }

  protected date(value: string): string {
    return new Date(value).toLocaleDateString('fr-FR', { day: 'numeric', month: 'long', year: 'numeric' });
  }

  private async run<T>(call: Observable<T>, done?: (result: T) => void): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    this.confirmation.set(null);
    try {
      const result = await firstValueFrom(call);
      done?.(result);
    } catch (error) {
      this.problem.set(describeSharingError(error));
    } finally {
      this.busy.set(false);
    }
    await this.load();
  }

  private async load(): Promise<void> {
    try {
      const [invitations, granted, received] = await Promise.all([
        firstValueFrom(this.api.invitations()),
        firstValueFrom(this.api.grantedShares()),
        firstValueFrom(this.api.receivedShares()),
      ]);
      this.invitations.set(invitations);
      this.granted.set(granted);
      this.received.set(received);
    } catch (error) {
      this.problem.set(describeSharingError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
