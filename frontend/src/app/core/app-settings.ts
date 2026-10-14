import { InjectionToken } from '@angular/core';

/**
 * Réglages de l'environnement. Fournis par injection pour qu'un test puisse
 * les remplacer sans toucher au code de production.
 */
export interface AppSettings {
  /** Origine de Catalogue.Api. Sert aussi à décider où le jeton est envoyé. */
  readonly apiBaseUrl: string;
  readonly oidc: {
    /** URL du realm Keycloak, qui est aussi l'issuer attendu. */
    readonly authority: string;
    /** Client public. Aucun secret ici, et il ne doit jamais y en avoir. */
    readonly clientId: string;
  };
}

const developmentSettings: AppSettings = {
  apiBaseUrl: 'http://localhost:5080',
  oidc: {
    authority: 'http://localhost:8080/realms/maison',
    clientId: 'catalogue-frontend',
  },
};

export const APP_SETTINGS = new InjectionToken<AppSettings>('AppSettings', {
  providedIn: 'root',
  factory: () => developmentSettings,
});
