import { inject } from '@angular/core';
import type { HttpInterceptorFn } from '@angular/common/http';
import { APP_SETTINGS } from '../app-settings';
import { AuthService } from './auth.service';

/**
 * Joint le jeton aux appels vers Catalogue.Api, et **à eux seuls**.
 *
 * Le filtre sur l'origine n'est pas une optimisation : sans lui, le jeton
 * partirait aussi vers toute autre origine appelée par l'application — un
 * CDN de polices, par exemple.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const settings = inject(APP_SETTINGS);

  if (!request.url.startsWith(settings.apiBaseUrl)) {
    return next(request);
  }

  const token = inject(AuthService).accessToken;
  if (token === null) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
