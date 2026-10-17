import { InjectionToken } from '@angular/core';

/**
 * Réglages de l'environnement. Fournis par injection pour qu'un test puisse
 * les remplacer sans toucher au code de production.
 */
export interface AppSettings {
  /** Origine de Catalogue.Api. Sert aussi à décider où le jeton est envoyé. */
  readonly apiBaseUrl: string;
  /** Origine de Tenants.Api (administration du ménage). Reçoit aussi le jeton. */
  readonly tenantsApiBaseUrl: string;
  readonly oidc: {
    /** URL du realm Keycloak, qui est aussi l'issuer attendu. */
    readonly authority: string;
    /** Client public. Aucun secret ici, et il ne doit jamais y en avoir. */
    readonly clientId: string;
  };
}

const developmentSettings: AppSettings = {
  apiBaseUrl: 'http://localhost:5080',
  tenantsApiBaseUrl: 'http://localhost:5082',
  oidc: {
    authority: 'http://localhost:8080/realms/maison',
    clientId: 'catalogue-frontend',
  },
};

/**
 * Réglages quand l'application est servie par le relais HTTPS du serveur de
 * développement (`tools/dev.sh front-lan`), pour l'essayer depuis un
 * téléphone du réseau local.
 *
 * Les API et Keycloak sont alors joints **par la même origine** que la page,
 * sous `/api`, `/tenants-api` et `/realms` : un téléphone ne peut pas joindre le `localhost`
 * du Mac, une page HTTPS ne peut pas appeler de l'HTTP, et la caméra exige
 * une page HTTPS.
 */
export function relayedSettings(origin: string): AppSettings {
  return {
    apiBaseUrl: `${origin}/api`,
    tenantsApiBaseUrl: `${origin}/tenants-api`,
    oidc: {
      authority: `${origin}/realms/maison`,
      clientId: 'catalogue-frontend',
    },
  };
}

export const APP_SETTINGS = new InjectionToken<AppSettings>('AppSettings', {
  providedIn: 'root',
  // Seul le relais sert l'application en HTTPS en développement : c'est ce
  // qui le désigne. Le serveur ordinaire, lui, reste en HTTP sur localhost.
  factory: () =>
    window.location.protocol === 'https:'
      ? relayedSettings(window.location.origin)
      : developmentSettings,
});
