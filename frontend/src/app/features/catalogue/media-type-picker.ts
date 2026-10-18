import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CatalogueApi } from '../../core/api/catalogue-api.service';
import { Icon, mediaTypeIcons } from '../../shared/icon';
import {
  mediaTypeLabels,
  mediaTypeSlugs,
  mediaTypes,
  type MediaType,
} from '../../core/api/media.models';

/**
 * Première étape de l'ajout : dire ce qu'on a dans les mains. Le reste de
 * l'écran en dépend — un livre se scanne par son ISBN, un film se cherche
 * par son titre, un jeu se saisit.
 */
@Component({
  selector: 'app-media-type-picker',
  imports: [RouterLink, Icon],
  template: `
    <h1>Ajouter un média</h1>
    <p class="lede">Qu’avez-vous dans les mains ?</p>

    <ul class="types">
      @for (type of types; track type) {
        <li>
          <a [routerLink]="['/catalogue/ajouter', slugs[type]]" [attr.data-type]="type">
            <app-icon [name]="icons[type]" size="1.75rem" />
            <span class="texte">
              <span class="nom">{{ labels[type] }}</span>
              <span class="comment">{{ how(type) }}</span>
            </span>
          </a>
        </li>
      }
    </ul>
  `,
  styles: `
    h1 {
      margin: 2.5rem 0 0.5rem;
    }

    .lede {
      color: var(--ink-soft);
      margin: 0 0 2rem;
    }

    .types {
      list-style: none;
      margin: 0;
      padding: 0;
      max-width: 34rem;
      border-top: 1px solid var(--shelf);
    }

    .types li {
      border-bottom: 1px solid var(--shelf);
    }

    /* Une tranche par type, avec le liseré de couleur de la liste du
       catalogue : le même code visuel d'un écran à l'autre. */
    a {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding: 0.9rem 0.9rem 0.9rem 1rem;
      border-inline-start: 4px solid var(--type-color, var(--shelf));
      color: var(--ink);
      text-decoration: none;
    }

    /* L'icône porte la couleur du type, comme le liseré. */
    app-icon {
      color: var(--type-color, var(--ink-soft));
    }

    .texte {
      display: flex;
      flex-direction: column;
      gap: 0.15rem;
    }

    a:hover .nom {
      color: var(--pine);
    }

    a[data-type='Book'] { --type-color: var(--type-book); }
    a[data-type='ComicBook'] { --type-color: var(--type-comic); }
    a[data-type='Music'] { --type-color: var(--type-music); }
    a[data-type='Film'] { --type-color: var(--type-film); }
    a[data-type='Game'] { --type-color: var(--type-game); }

    .nom {
      font-family: var(--serif);
      font-size: 1.125rem;
    }

    .comment {
      font-size: 0.875rem;
      color: var(--ink-soft);
    }
  `,
})
export class MediaTypePicker implements OnInit {
  private readonly api = inject(CatalogueApi);

  protected readonly types = mediaTypes;
  protected readonly labels = mediaTypeLabels;
  protected readonly slugs = mediaTypeSlugs;
  protected readonly icons = mediaTypeIcons;

  /** La recherche de films n'existe que si le backend a une clé OMDb : on ne la promet pas sans. */
  private readonly filmSearch = signal(false);

  async ngOnInit(): Promise<void> {
    try {
      this.filmSearch.set((await firstValueFrom(this.api.filmSearchAvailable())).enabled);
    } catch {
      this.filmSearch.set(false);
    }
  }

  protected how(type: MediaType): string {
    switch (type) {
      case 'Book':
      case 'ComicBook':
        return 'Scanner l’ISBN, ou saisir à la main';
      case 'Music':
        return 'Scanner le code-barres, ou saisir à la main';
      case 'Film':
        return this.filmSearch() ? 'Chercher par le titre, ou saisir à la main' : 'Saisir à la main';
      case 'Game':
        return 'Saisir à la main';
    }
  }
}
