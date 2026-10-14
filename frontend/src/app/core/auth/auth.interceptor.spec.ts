import { provideHttpClient, withInterceptors, HttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { APP_SETTINGS, type AppSettings } from '../app-settings';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

const settings: AppSettings = {
  apiBaseUrl: 'http://catalogue.test',
  oidc: { authority: 'http://keycloak.test/realms/maison', clientId: 'catalogue-frontend' },
};

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: APP_SETTINGS, useValue: settings },
        { provide: AuthService, useValue: { accessToken: 'jeton-de-test' } as unknown as AuthService },
      ],
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  it('joint le jeton aux appels vers le catalogue', () => {
    http.get(`${settings.apiBaseUrl}/medias`).subscribe();

    const requete = backend.expectOne(`${settings.apiBaseUrl}/medias`);
    expect(requete.request.headers.get('Authorization')).toBe('Bearer jeton-de-test');
    requete.flush({ items: [], total: 0, page: 1, size: 50 });
  });

  it("n'envoie le jeton à aucune autre origine", () => {
    // Règle de sécurité : sans ce filtre, le jeton partirait vers toute
    // origine appelée par l'application — un CDN de polices, par exemple.
    http.get('https://fonts.googleapis.com/css2').subscribe();

    const requete = backend.expectOne('https://fonts.googleapis.com/css2');
    expect(requete.request.headers.has('Authorization')).toBe(false);
    requete.flush('');
  });

  afterEach(() => backend.verify());
});
