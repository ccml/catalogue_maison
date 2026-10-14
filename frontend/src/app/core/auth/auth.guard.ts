import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Protège une route. Ce n'est pas une mesure de sécurité — elle évite
 * simplement d'afficher un écran qui n'aurait rien à montrer. L'API refuse
 * de toute façon une requête sans jeton valide.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);

  if (auth.isAuthenticated()) {
    return true;
  }

  return inject(Router).createUrlTree(['/connexion'], {
    queryParams: { suite: state.url },
  });
};
