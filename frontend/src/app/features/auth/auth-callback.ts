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
    } catch (cause) {
      // Le message destiné à l'utilisateur reste court, mais la cause est
      // journalisée : l'avoir avalée a déjà coûté un aller-retour de
      // diagnostic, alors que le log de Keycloak disait tout.
      console.error('Échec de l\'échange du code contre un jeton.', cause);

      const detail = cause instanceof Error ? cause.message : String(cause);
      this.error.set(`Le fournisseur d'identité a refusé la connexion : ${detail}`);
    }
  }
}
