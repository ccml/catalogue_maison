import { inject } from '@angular/core';
import { Router, type CanActivateFn, type Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { AuthService } from './core/auth/auth.service';

/**
 * L'opérateur n'a pas de catalogue : on l'envoie vers les ménages. Utile
 * surtout après la connexion, dont la destination a été choisie avant de
 * savoir qui se connectait. Simple aiguillage, l'API tranche les droits.
 */
const operatorToHouseholds: CanActivateFn = () =>
  inject(AuthService).mayAdministerPlatform() ? inject(Router).createUrlTree(['/menages']) : true;

export const routes: Routes = [
  {
    // L'opérateur n'a pas de catalogue : sa page d'accueil est celle des
    // ménages. Simple aiguillage d'affichage, l'API tranche les droits.
    path: '',
    pathMatch: 'full',
    redirectTo: () => (inject(AuthService).mayAdministerPlatform() ? 'menages' : 'catalogue'),
  },
  {
    path: 'catalogue',
    title: 'Catalogue',
    canActivate: [authGuard, operatorToHouseholds],
    loadComponent: () => import('./features/catalogue/media-list').then((m) => m.MediaList),
  },
  {
    path: 'catalogue/ajouter',
    title: 'Ajouter un média',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/catalogue/media-type-picker').then((m) => m.MediaTypePicker),
  },
  {
    // Le type est choisi d'abord : l'écran de saisie en dépend.
    path: 'catalogue/ajouter/:kind',
    title: 'Ajouter un média',
    canActivate: [authGuard],
    loadComponent: () => import('./features/catalogue/media-form').then((m) => m.MediaForm),
  },
  {
    path: 'catalogue/:id',
    title: 'Corriger une fiche',
    canActivate: [authGuard],
    loadComponent: () => import('./features/catalogue/media-form').then((m) => m.MediaForm),
  },
  {
    // Réservé à l'administrateur du ménage. Le garde n'est qu'un confort :
    // Tenants.Api refuse de toute façon un compte qui n'a pas le rôle.
    path: 'menage',
    title: 'Membres du ménage',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/household/household-members').then((m) => m.HouseholdMembers),
  },
  {
    // Réservé à l'opérateur de la plateforme. Comme pour « menage », le
    // garde n'est qu'un confort : Tenants.Api refuse tout autre compte.
    path: 'menages',
    title: 'Ménages',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/platform/platform-households').then((m) => m.PlatformHouseholds),
  },
  {
    path: 'connexion',
    title: 'Connexion',
    loadComponent: () => import('./features/auth/sign-in').then((m) => m.SignIn),
  },
  {
    path: 'connexion/retour',
    loadComponent: () => import('./features/auth/auth-callback').then((m) => m.AuthCallback),
  },
  { path: '**', redirectTo: 'catalogue' },
];
