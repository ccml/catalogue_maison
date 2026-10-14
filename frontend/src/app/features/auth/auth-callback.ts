import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Atterrissage au retour de Keycloak. N'affiche rien d'utile : il échange le
 * code contre un jeton et s'en va.
 */
@Component({
  selector: 'app-auth-callback',
  template: `
    @if (error()) {
      <section class="callback">
        <h1>La connexion n'a pas abouti</h1>
        <p>{{ error() }}</p>
        <a href="/connexion">Réessayer</a>
      </section>
    } @else {
      <p class="callback">Connexion en cours…</p>
    }
  `,
  styles: `
    .callback {
      padding: 4rem 0;
      color: var(--ink-soft);
    }
  `,
})
export class AuthCallback {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.complete();
  }

  private async complete(): Promise<void> {
    try {
      const suite = await this.auth.completeSignIn();
      await this.router.navigateByUrl(suite);
    } catch {
      // Le détail vient de Keycloak et n'aide pas l'utilisateur ; il est
      // dans la console du navigateur pour qui débogue.
      this.error.set('Le fournisseur d\'identité a refusé la connexion.');
    }
  }
}
