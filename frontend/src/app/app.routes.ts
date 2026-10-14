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
    loadComponent: () => import('./features/catalogue/media-form').then((m) => m.MediaForm),
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
