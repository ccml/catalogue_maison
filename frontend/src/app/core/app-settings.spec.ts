import { describe, expect, it } from 'vitest';
import { relayedSettings } from './app-settings';

describe('relayedSettings', () => {
  it('joint l’API et Keycloak par l’origine de la page', () => {
    const settings = relayedSettings('https://192.168.1.25:4443');

    expect(settings.apiBaseUrl).toBe('https://192.168.1.25:4443/api');
    expect(settings.oidc.authority).toBe('https://192.168.1.25:4443/realms/maison');
  });

  it('ne fait pas passer Keycloak pour l’API, qui seule reçoit le jeton', () => {
    // L'intercepteur joint le jeton à ce qui commence par apiBaseUrl : les
    // appels à Keycloak, sur la même origine, ne doivent pas en faire partie.
    const settings = relayedSettings('https://192.168.1.25:4443');

    expect(settings.oidc.authority.startsWith(settings.apiBaseUrl)).toBe(false);
  });
});
