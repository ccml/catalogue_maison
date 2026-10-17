import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CatalogueApi } from '../../core/api/catalogue-api.service';
import { describeHttpError } from '../../core/api/http-error';
import {
  mediaTypeLabels,
  mediaTypes,
  type CreateMediaRequest,
  type MediaDetail,
  type MediaType,
  type ScanDraft,
  type ScanResult,
} from '../../core/api/media.models';
import { BarcodeScanner } from './barcode-scanner';
import { buildSpecifics, parseNumber, specificsToFields, text } from './media-form.specifics';
import { normalizeCode, scanNotice } from './scan';

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
 */
@Component({
  selector: 'app-media-form',
  imports: [ReactiveFormsModule, RouterLink, BarcodeScanner],
  templateUrl: './media-form.html',
  styleUrl: './media-form.scss',
})
export class MediaForm implements OnInit {
  /** Fourni par la route en correction, absent en saisie. */
  readonly id = input<string>();

  private readonly api = inject(CatalogueApi);
  private readonly router = inject(Router);

  protected readonly types = mediaTypes;
  protected readonly labels = mediaTypeLabels;

  protected readonly correction = computed(() => this.id() !== undefined);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly removing = signal(false);
  protected readonly suppressionDemandee = signal(false);
  protected readonly probleme = signal<string | null>(null);
  protected readonly currentType = signal<MediaType>('Book');

  protected readonly camera = signal(false);
  protected readonly searching = signal(false);
  protected readonly scanResult = signal<ScanResult | null>(null);
  protected readonly scanProbleme = signal<string | null>(null);
  protected readonly notice = computed(() => {
    const result = this.scanResult();
    return result === null ? null : scanNotice(result);
  });

  private readonly fb = inject(FormBuilder).nonNullable;

  /**
   * Code tapé, ou envoyé par une douchette qui se comporte comme un clavier
   * et finit par Entrée. Un vrai groupe, et non un contrôle isolé : sans
   * `[formGroup]`, Entrée déclencherait la soumission native du navigateur,
   * qui recharge la page et perd le jeton tenu en mémoire.
   */
  protected readonly scanForm = this.fb.group({ code: [''] });

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
    series: [''],
    volume: [''],
    illustrator: [''],
    label: [''],
    format: [''],
    tracks: [''],
    studio: [''],
    durationMinutes: [''],
    support: [''],
    platform: [''],
    minPlayers: [''],
    maxPlayers: [''],
  });

  constructor() {
    this.form.controls.type.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((type) => this.currentType.set(type));
  }

  async ngOnInit(): Promise<void> {
    const id = this.id();
    if (id === undefined) {
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

  protected async submit(): Promise<void> {
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
      await this.router.navigate(['/catalogue']);
    } catch (error) {
      this.probleme.set(this.describe(error));
    } finally {
      this.saving.set(false);
    }
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

  private async scanne(code: string): Promise<void> {
    this.searching.set(true);
    this.scanProbleme.set(null);
    this.scanResult.set(null);

    try {
      const result = await firstValueFrom(this.api.scan(code));
      this.scanResult.set(result);

      // Champs absents quand ils sont nuls : on teste l'absence, pas `null`.
      if (!result.existing && result.draft) {
        this.preRemplis(result.draft);
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
    const champs = specificsToFields(draft.specifics ?? null);
    const nombre = (valeur: number | null): string => (valeur === null ? '' : String(valeur));

    this.form.patchValue({
      type: draft.type,
      title: draft.title ?? '',
      creator: draft.creator ?? '',
      year: nombre(draft.year ?? null),
      isbn: champs.isbn,
      publisher: champs.publisher,
      pages: nombre(champs.pages),
      series: champs.series,
      volume: nombre(champs.volume),
      illustrator: champs.illustrator,
      label: champs.label,
      format: champs.format,
      tracks: champs.tracks,
      studio: champs.studio,
      durationMinutes: nombre(champs.durationMinutes),
      support: champs.support,
      platform: champs.platform,
      minPlayers: nombre(champs.minPlayers),
      maxPlayers: nombre(champs.maxPlayers),
    });

    this.currentType.set(draft.type);
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
        series: value.series,
        volume: parseNumber(value.volume),
        illustrator: value.illustrator,
        label: value.label,
        format: value.format,
        tracks: value.tracks,
        studio: value.studio,
        durationMinutes: parseNumber(value.durationMinutes),
        support: value.support,
        platform: value.platform,
        minPlayers: parseNumber(value.minPlayers),
        maxPlayers: parseNumber(value.maxPlayers),
      }),
    };
  }

  private remplis(media: MediaDetail): void {
    const champs = specificsToFields(media.specifics);
    const nombre = (valeur: number | null): string => (valeur === null ? '' : String(valeur));

    this.form.setValue({
      type: media.type,
      title: media.title,
      creator: media.creator ?? '',
      year: nombre(media.year),
      location: media.location ?? '',
      isbn: champs.isbn,
      publisher: champs.publisher,
      pages: nombre(champs.pages),
      series: champs.series,
      volume: nombre(champs.volume),
      illustrator: champs.illustrator,
      label: champs.label,
      format: champs.format,
      tracks: champs.tracks,
      studio: champs.studio,
      durationMinutes: nombre(champs.durationMinutes),
      support: champs.support,
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
