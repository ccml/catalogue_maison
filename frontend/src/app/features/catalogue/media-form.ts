import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CatalogueApi } from '../../core/api/catalogue-api.service';
import { describeHttpError } from '../../core/api/http-error';
import {
  mediaTypeLabels,
  type CreateMediaRequest,
  type MediaType,
} from '../../core/api/media.models';
import { buildSpecifics, parseNumber, text } from './media-form.specifics';

/**
 * Saisie manuelle. **Seuls le type et le titre sont requis** : on doit
 * pouvoir enregistrer un objet qu'on a dans les mains sans rien savoir
 * d'autre de lui.
 */
@Component({
  selector: 'app-media-form',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './media-form.html',
  styleUrl: './media-form.scss',
})
export class MediaForm {
  private readonly api = inject(CatalogueApi);
  private readonly router = inject(Router);

  protected readonly types: readonly MediaType[] = ['Book', 'Music', 'Film', 'Game'];
  protected readonly labels = mediaTypeLabels;

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly currentType = signal<MediaType>('Book');

  // Tous les champs sont du texte : les nombres sont convertis à
  // l'enregistrement, ce qui évite des contrôles nullables et des saisies
  // intermédiaires invalides.
  protected readonly form = inject(FormBuilder).nonNullable.group({
    type: ['Book' as MediaType, Validators.required],
    title: ['', [Validators.required, Validators.maxLength(512)]],
    creator: [''],
    year: [''],
    location: [''],
    isbn: [''],
    publisher: [''],
    pages: [''],
    label: [''],
    format: [''],
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

  protected get titleControl() {
    return this.form.controls.title;
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      return;
    }

    const value = this.form.getRawValue();
    const type = value.type;

    const request: CreateMediaRequest = {
      type,
      title: value.title.trim(),
      creator: text(value.creator),
      year: parseNumber(value.year),
      location: text(value.location),
      specifics: buildSpecifics(type, {
        isbn: value.isbn,
        publisher: value.publisher,
        pages: parseNumber(value.pages),
        label: value.label,
        format: value.format,
        studio: value.studio,
        durationMinutes: parseNumber(value.durationMinutes),
        support: value.support,
        platform: value.platform,
        minPlayers: parseNumber(value.minPlayers),
        maxPlayers: parseNumber(value.maxPlayers),
      }),
    };

    this.saving.set(true);
    this.error.set(null);

    try {
      await firstValueFrom(this.api.create(request));
      await this.router.navigate(['/catalogue']);
    } catch (error) {
      this.error.set(this.describe(error));
    } finally {
      this.saving.set(false);
    }
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
