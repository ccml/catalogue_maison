import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CatalogueApi } from '../../core/api/catalogue-api.service';
import { describeHttpError } from '../../core/api/http-error';
import {
  addTitles,
  bookGenres,
  creatorLabels,
  mediaTypeFromSlug,
  mediaTypeLabels,
  mediaTypes,
  type CreateMediaRequest,
  type FilmCandidate,
  type MediaDetail,
  type MediaType,
  type ScanDraft,
  type ScanResult,
} from '../../core/api/media.models';
import { Icon } from '../../shared/icon';
import { BarcodeScanner } from './barcode-scanner';
import {
  buildSpecifics,
  lineNumbers,
  parseNumber,
  specificsToFields,
  text,
} from './media-form.specifics';
import {
  draftType,
  filmDraftNotice,
  filmSearchNotice,
  isIsbn,
  normalizeCode,
  scanNotice,
  type ScanNotice,
} from './scan';

/**
 * Saisie et correction d'une fiche. **Seuls le type et le titre sont
 * requis** : on doit pouvoir enregistrer un objet qu'on a dans les mains
 * sans rien savoir d'autre de lui.
 *
 * Le même écran sert aux deux gestes, parce que corriger c'est resaisir : un
 * formulaire de correction distinct finirait par diverger du formulaire de
 * saisie, et un champ oublié d'un côté effacerait des données de l'autre.
 *
 * Le scan vit ici pour la même raison : il ne fait que **pré-remplir** ce
 * formulaire, que l'utilisateur valide ou corrige avant d'enregistrer. Un
 * scan sans notice laisse simplement le formulaire à compléter.
 *
 * En saisie, le type a été choisi à l'étape précédente
 * (`/catalogue/ajouter/:kind`), et l'écran n'offre que ce qui a du sens pour
 * lui : le scan de l'ISBN pour un livre ou une BD, du code-barres pour un
 * disque, la recherche par titre pour un film. En correction, le type reste
 * modifiable, pour réparer une erreur de saisie.
 */
@Component({
  selector: 'app-media-form',
  imports: [ReactiveFormsModule, RouterLink, BarcodeScanner, Icon],
  templateUrl: './media-form.html',
  styleUrl: './media-form.scss',
})
export class MediaForm implements OnInit {
  /** Fourni par la route en correction, absent en saisie. */
  readonly id = input<string>();

  /** Type choisi à l'étape précédente, en saisie : segment d'URL (`livre`, `bd`…). */
  readonly kind = input<string>();

  /** Type choisi pour la saisie ; `null` en correction, ou si l'URL n'en désigne aucun. */
  protected readonly chosenType = computed(() => mediaTypeFromSlug(this.kind()));

  /** Le scan n'a de source que pour les livres, les BD et les disques. */
  protected readonly offersScan = computed(() => {
    const type = this.chosenType();
    return type === 'Book' || type === 'ComicBook' || type === 'Music';
  });

  protected readonly addTitles = addTitles;
  protected readonly creatorLabels = creatorLabels;

  private readonly api = inject(CatalogueApi);
  private readonly router = inject(Router);

  protected readonly types = mediaTypes;
  protected readonly labels = mediaTypeLabels;
  protected readonly genres = bookGenres;

  protected readonly correction = computed(() => this.id() !== undefined);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly removing = signal(false);
  protected readonly suppressionDemandee = signal(false);
  protected readonly probleme = signal<string | null>(null);
  /** Titre du dernier média ajouté par « Enregistrer et ajouter un autre ». */
  protected readonly dernierAjout = signal<string | null>(null);
  protected readonly currentType = signal<MediaType>('Book');
  /** Numéro affiché devant chaque ligne du champ des pistes. */
  protected readonly trackNumbers = signal<readonly (number | null)[]>([null]);

  protected readonly camera = signal(false);
  protected readonly searching = signal(false);
  protected readonly scanResult = signal<ScanResult | null>(null);
  protected readonly scanProbleme = signal<string | null>(null);
  protected readonly notice = computed(() => {
    const result = this.scanResult();
    return result === null ? null : scanNotice(result, this.chosenType());
  });

  /** La recherche de films n'existe que si le backend a une clé OMDb. */
  protected readonly filmSearchAvailable = signal(false);
  protected readonly filmSearching = signal(false);
  protected readonly films = signal<readonly FilmCandidate[] | null>(null);
  protected readonly filmNotice = signal<ScanNotice | null>(null);

  private readonly fb = inject(FormBuilder).nonNullable;

  /**
   * Code tapé, ou envoyé par une douchette qui se comporte comme un clavier
   * et finit par Entrée. Un vrai groupe, et non un contrôle isolé : sans
   * `[formGroup]`, Entrée déclencherait la soumission native du navigateur,
   * qui recharge la page et perd le jeton tenu en mémoire.
   */
  protected readonly scanForm = this.fb.group({ code: [''] });

  /** Titre d'un film à chercher. Même raison que pour le code : un vrai groupe. */
  protected readonly filmForm = this.fb.group({ title: [''] });

  // Tous les champs sont du texte : les nombres sont convertis à
  // l'enregistrement, ce qui évite des contrôles nullables et des saisies
  // intermédiaires invalides.
  protected readonly form = this.fb.group({
    type: ['Book' as MediaType, Validators.required],
    title: ['', [Validators.required, Validators.maxLength(512)]],
    creator: [''],
    year: [''],
    location: [''],
    isbn: [''],
    publisher: [''],
    pages: [''],
    collection: [''],
    collectionNumber: [''],
    genre: [''],
    edition: [''],
    series: [''],
    volume: [''],
    illustrator: [''],
    label: [''],
    format: [''],
    tracks: [''],
    barcode: [''],
    studio: [''],
    durationMinutes: [''],
    support: [''],
    actors: [''],
    platform: [''],
    minPlayers: [''],
    maxPlayers: [''],
  });

  constructor() {
    this.form.controls.type.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((type) => this.currentType.set(type));
    this.form.controls.tracks.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((tracks) => this.trackNumbers.set(lineNumbers(tracks)));
  }

  async ngOnInit(): Promise<void> {
    const id = this.id();
    if (id === undefined) {
      const type = this.chosenType();
      if (type === null) {
        // Adresse qui ne désigne aucun type : retour au choix du type.
        await this.router.navigate(['/catalogue/ajouter']);
        return;
      }

      this.form.controls.type.setValue(type);
      if (type === 'Film') {
        await this.verifieRechercheDeFilms();
      }
      return;
    }

    this.loading.set(true);
    try {
      this.remplis(await firstValueFrom(this.api.get(id)));
    } catch (error) {
      this.probleme.set(describeHttpError(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  protected get titleControl() {
    return this.form.controls.title;
  }

  /**
   * Enregistre la fiche. `continuer` sert la saisie en série — une pile de
   * BD, une étagère de disques : au lieu de revenir au catalogue, on repart
   * d'une fiche vierge du même type.
   */
  protected async submit(continuer = false): Promise<void> {
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      return;
    }

    const requete = this.requete();
    const id = this.id();

    this.saving.set(true);
    this.probleme.set(null);

    try {
      await firstValueFrom(id === undefined ? this.api.create(requete) : this.api.update(id, requete));
      if (continuer && id === undefined) {
        this.repartDeZero(requete.title);
      } else {
        await this.router.navigate(['/catalogue']);
      }
    } catch (error) {
      this.probleme.set(this.describe(error));
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * Fiche vierge pour le média suivant. Le type et l'emplacement sont
   * gardés : une série se saisit d'ordinaire d'une même étagère.
   */
  private repartDeZero(titre: string): void {
    const type = this.form.controls.type.value;
    const location = this.form.controls.location.value;

    this.form.reset({ type, location });
    this.scanForm.reset();
    this.filmForm.reset();
    this.scanResult.set(null);
    this.scanProbleme.set(null);
    this.films.set(null);
    this.filmNotice.set(null);
    this.dernierAjout.set(titre);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected async chercheCode(): Promise<void> {
    const code = normalizeCode(this.scanForm.controls.code.value);
    if (code === null) {
      this.scanResult.set(null);
      this.scanProbleme.set(
        'Ce code n’a pas la forme d’un ISBN ni d’un code-barres : 8, 10, 12 ou 13 chiffres.',
      );
      return;
    }

    await this.scanne(code);
  }

  protected async codeLu(code: string): Promise<void> {
    this.camera.set(false);
    this.scanForm.controls.code.setValue(code);
    await this.scanne(code);
  }

  /**
   * Demande au backend si la recherche de films est en service. En cas
   * d'échec, elle reste simplement masquée : ce n'est qu'une commodité.
   */
  private async verifieRechercheDeFilms(): Promise<void> {
    try {
      this.filmSearchAvailable.set((await firstValueFrom(this.api.filmSearchAvailable())).enabled);
    } catch {
      this.filmSearchAvailable.set(false);
    }
  }

  protected async chercheFilm(): Promise<void> {
    const title = this.filmForm.controls.title.value.trim();
    if (title.length < 2) {
      this.films.set(null);
      this.filmNotice.set({ text: 'Indiquez au moins deux lettres du titre.', tone: 'attention' });
      return;
    }

    this.filmSearching.set(true);
    this.filmNotice.set(null);
    this.films.set(null);

    try {
      const result = await firstValueFrom(this.api.searchFilms(title));
      this.films.set(result.films);
      this.filmNotice.set(filmSearchNotice(result));
    } catch (error) {
      this.filmNotice.set({ text: describeHttpError(error).message, tone: 'attention' });
    } finally {
      this.filmSearching.set(false);
    }
  }

  protected async choisisFilm(film: FilmCandidate): Promise<void> {
    this.filmSearching.set(true);
    this.filmNotice.set(null);

    try {
      const result = await firstValueFrom(this.api.film(film.id));
      this.preRemplis(result.draft);
      this.films.set(null);
      this.scanResult.set(null);
      this.filmNotice.set(filmDraftNotice(result.source));
    } catch (error) {
      this.filmNotice.set({ text: describeHttpError(error).message, tone: 'attention' });
    } finally {
      this.filmSearching.set(false);
    }
  }

  private async scanne(code: string): Promise<void> {
    this.dernierAjout.set(null);
    this.searching.set(true);
    this.scanProbleme.set(null);
    this.scanResult.set(null);

    try {
      const result = await firstValueFrom(this.api.scan(code));
      this.scanResult.set(result);

      // Champs absents quand ils sont nuls : on teste l'absence, pas `null`.
      if (!result.existing && result.draft) {
        this.preRemplis(result.draft);
      } else if (!result.existing && !isIsbn(result.code)) {
        // Code-barres inconnu des sources : on ne sait pas si c'est un
        // disque, mais s'il en est un, son code doit être noté pour qu'il
        // soit reconnu au prochain scan.
        this.form.controls.barcode.setValue(result.code);
      }
    } catch (error) {
      this.scanProbleme.set(describeHttpError(error).message);
    } finally {
      this.searching.set(false);
    }
  }

  /**
   * Reporte la fiche proposée dans le formulaire. Tous les champs de
   * spécificités sont réécrits, pour qu'un scan précédent ne laisse pas de
   * restes ; l'emplacement est conservé, seul le ménage le connaît.
   */
  private preRemplis(draft: ScanDraft): void {
    const type = draftType(draft.type, this.chosenType());
    const champs = specificsToFields(draft.specifics ?? null);
    const nombre = (valeur: number | null): string => (valeur === null ? '' : String(valeur));

    this.form.patchValue({
      type,
      title: draft.title ?? '',
      creator: draft.creator ?? '',
      year: nombre(draft.year ?? null),
      isbn: champs.isbn,
      publisher: champs.publisher,
      pages: nombre(champs.pages),
      collection: champs.collection,
      collectionNumber: champs.collectionNumber,
      genre: champs.genre,
      edition: champs.edition,
      series: champs.series,
      volume: nombre(champs.volume),
      illustrator: champs.illustrator,
      label: champs.label,
      format: champs.format,
      tracks: champs.tracks,
      barcode: champs.barcode,
      studio: champs.studio,
      durationMinutes: nombre(champs.durationMinutes),
      support: champs.support,
      actors: champs.actors,
      platform: champs.platform,
      minPlayers: nombre(champs.minPlayers),
      maxPlayers: nombre(champs.maxPlayers),
    });

    this.currentType.set(type);
  }

  /** Premier clic : la suppression étant définitive, elle se confirme. */
  protected demandeSuppression(): void {
    this.suppressionDemandee.set(true);
  }

  protected annuleSuppression(): void {
    this.suppressionDemandee.set(false);
  }

  protected async supprime(): Promise<void> {
    const id = this.id();
    if (id === undefined) {
      return;
    }

    this.removing.set(true);
    this.probleme.set(null);

    try {
      await firstValueFrom(this.api.remove(id));
      await this.router.navigate(['/catalogue']);
    } catch (error) {
      this.probleme.set(this.describe(error));
      this.suppressionDemandee.set(false);
    } finally {
      this.removing.set(false);
    }
  }

  private requete(): CreateMediaRequest {
    const value = this.form.getRawValue();
    const type = value.type;

    return {
      type,
      title: value.title.trim(),
      creator: text(value.creator),
      year: parseNumber(value.year),
      location: text(value.location),
      specifics: buildSpecifics(type, {
        isbn: value.isbn,
        publisher: value.publisher,
        pages: parseNumber(value.pages),
        collection: value.collection,
        collectionNumber: value.collectionNumber,
        genre: value.genre,
        edition: value.edition,
        series: value.series,
        volume: parseNumber(value.volume),
        illustrator: value.illustrator,
        label: value.label,
        format: value.format,
        tracks: value.tracks,
        barcode: value.barcode,
        studio: value.studio,
        durationMinutes: parseNumber(value.durationMinutes),
        support: value.support,
        actors: value.actors,
        platform: value.platform,
        minPlayers: parseNumber(value.minPlayers),
        maxPlayers: parseNumber(value.maxPlayers),
      }),
    };
  }

  private remplis(media: MediaDetail): void {
    const champs = specificsToFields(media.specifics ?? null);
    const nombre = (valeur: number | null): string => (valeur === null ? '' : String(valeur));

    this.form.setValue({
      type: media.type,
      title: media.title,
      creator: media.creator ?? '',
      year: nombre(media.year ?? null),
      location: media.location ?? '',
      isbn: champs.isbn,
      publisher: champs.publisher,
      pages: nombre(champs.pages),
      collection: champs.collection,
      collectionNumber: champs.collectionNumber,
      genre: champs.genre,
      edition: champs.edition,
      series: champs.series,
      volume: nombre(champs.volume),
      illustrator: champs.illustrator,
      label: champs.label,
      format: champs.format,
      tracks: champs.tracks,
      barcode: champs.barcode,
      studio: champs.studio,
      durationMinutes: nombre(champs.durationMinutes),
      support: champs.support,
      actors: champs.actors,
      platform: champs.platform,
      minPlayers: nombre(champs.minPlayers),
      maxPlayers: nombre(champs.maxPlayers),
    });

    this.currentType.set(media.type);
  }

  /**
   * L'API renvoie ses refus de validation sous forme de liste : on les
   * montre tels quels plutôt que de les remplacer par un message générique.
   */
  private describe(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 400) {
      const problem = error.error as { errors?: Record<string, string[]> } | null;
      const messages = problem?.errors?.['media'] ?? [];

      if (messages.length > 0) {
        return messages.join(' ');
      }
    }

    return describeHttpError(error).message;
  }
}
