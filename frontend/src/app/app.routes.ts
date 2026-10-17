import type { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'catalogue' },
  {
    path: 'catalogue',
    title: 'Catalogue',
    canActivate: [authGuard],
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
