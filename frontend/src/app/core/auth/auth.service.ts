import { Injectable, computed, inject, signal } from '@angular/core';
import {
  InMemoryWebStorage,
  User,
  UserManager,
  WebStorageStateStore,
  type UserManagerSettings,
} from 'oidc-client-ts';
import { APP_SETTINGS } from '../app-settings';
import {
  hasHousehold,
  householdOf,
  mayWrite,
  rolesOf,
  type KeycloakClaims,
} from './keycloak-claims';

/**
 * Authentification par Authorization Code Flow + PKCE, client public.
 *
 * Deux stockages, délibérément distincts :
 *
 * - les **jetons** vivent en mémoire uniquement. Rien dans `localStorage` :
 *   un jeton écrit sur disque survit à l'onglet et devient lisible par tout
 *   script de la page.
 * - l'**état de la redirection** (dont le `code_verifier` PKCE) doit, lui,
 *   survivre à l'aller-retour vers Keycloak, sinon le flux ne peut pas
 *   aboutir. Il va donc dans `sessionStorage`, et rien d'autre n'y va.
 *
 * Conséquence assumée : recharger la page perd la session côté navigateur.
 * La reconnexion est un aller-retour invisible tant que la session Keycloak
 * est vivante — pas de saisie de mot de passe. L'alternative serait de
 * persister le jeton, ce que le projet refuse.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly settings = inject(APP_SETTINGS);
  private readonly manager = new UserManager(this.buildSettings());
  private readonly user = signal<User | null>(null);

  readonly isAuthenticated = computed(() => this.user() !== null);

  readonly displayName = computed(() => {
    const claims = this.claims();
    return claims?.name ?? claims?.preferred_username ?? '';
  });

  /** Ménage, pour l'affichage seulement. */
  readonly household = computed(() => householdOf(this.claims()));

  /** Vrai si l'utilisateur a un ménage, donc un catalogue à consulter. */
  readonly hasHousehold = computed(() => hasHousehold(this.claims()));

  /** Rôles, pour l'affichage seulement — jamais comme règle de sécurité. */
  readonly rolesForDisplay = computed<readonly string[]>(() => rolesOf(this.claims()));

  /** Vrai si l'interface peut proposer d'écrire. L'API tranche vraiment. */
  readonly mayWrite = computed(() => mayWrite(this.claims()));

  constructor() {
    this.manager.events.addUserLoaded((user) => this.user.set(user));
    this.manager.events.addUserUnloaded(() => this.user.set(null));
    this.manager.events.addAccessTokenExpired(() => this.user.set(null));
  }

  /** Jeton courant, lu à chaque requête plutôt que mémorisé ailleurs. */
  get accessToken(): string | null {
    return this.user()?.access_token ?? null;
  }

  /** Reprend une session déjà en mémoire, s'il y en a une. */
  async restore(): Promise<void> {
    const user = await this.manager.getUser();
    this.user.set(user?.expired === false ? user : null);
  }

  /** Part vers Keycloak. `returnTo` est restitué au retour. */
  async signIn(returnTo?: string): Promise<void> {
    await this.manager.signinRedirect({ state: returnTo ?? '/' });
  }

  /**
   * Termine le flux au retour de Keycloak et rend la route à rejoindre.
   */
  async completeSignIn(): Promise<string> {
    const user = await this.manager.signinRedirectCallback();
    this.user.set(user);
    return typeof user.state === 'string' && user.state.startsWith('/') ? user.state : '/';
  }

  async signOut(): Promise<void> {
    this.user.set(null);
    await this.manager.signoutRedirect();
  }

  private claims(): KeycloakClaims | null {
    const profile = this.user()?.profile;
    return profile ? (profile as unknown as KeycloakClaims) : null;
  }

  private buildSettings(): UserManagerSettings {
    const origin = window.location.origin;

    return {
      authority: this.settings.oidc.authority,
      client_id: this.settings.oidc.clientId,
      redirect_uri: `${origin}/connexion/retour`,
      post_logout_redirect_uri: origin,
      // Code Flow. oidc-client-ts applique PKCE avec S256 par défaut ;
      // ne jamais basculer sur l'Implicit Flow.
      response_type: 'code',
      scope: 'openid profile email',
      // Le renouvellement passe par le refresh token tenu en mémoire, pas
      // par une iframe cachée.
      automaticSilentRenew: true,
      monitorSession: false,
      userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
      stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
    };
  }
}
