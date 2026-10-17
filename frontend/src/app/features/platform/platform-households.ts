import { Component, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom, map, type Observable } from 'rxjs';
import {
  describeHouseholdError,
  householdIdMaxLength,
  householdIdPattern,
  householdStatusLabels,
  suggestHouseholdId,
  type Household,
} from '../../core/api/household.models';
import { temporaryPasswordMinLength, type MemberProblem } from '../../core/api/member.models';
import { TenantsApi } from '../../core/api/tenants-api.service';

/**
 * Administration des ménages, réservée à l'opérateur de la plateforme.
 *
 * Créer un ménage, c'est l'inscrire au registre puis faire créer son
 * catalogue : si la seconde étape échoue, le ménage reste « en cours » et se
 * reprend d'un clic. Il reçoit ensuite son premier administrateur, qui gère
 * lui-même les autres membres.
 */
@Component({
  selector: 'app-platform-households',
  imports: [ReactiveFormsModule],
  templateUrl: './platform-households.html',
  // Même registre visuel que l'écran des membres : lignes, formulaires et
  // messages y sont déjà définis.
  styleUrls: ['../household/household-members.scss', './platform-households.scss'],
})
export class PlatformHouseholds implements OnInit {
  private readonly api = inject(TenantsApi);
  private readonly fb = inject(FormBuilder);

  protected readonly households = signal<readonly Household[]>([]);
  protected readonly loading = signal(true);
  protected readonly problem = signal<MemberProblem | null>(null);
  protected readonly confirmation = signal<string | null>(null);
  protected readonly busy = signal<string | null>(null);

  /** Ménage dont on saisit un administrateur, s'il y en a un. */
  protected readonly addingAdminTo = signal<string | null>(null);

  /** Ménage dont on confirme la suppression définitive, s'il y en a un. */
  protected readonly purging = signal<string | null>(null);

  protected readonly statusLabels = householdStatusLabels;

  protected readonly idMaxLength = householdIdMaxLength;
  protected readonly minLength = temporaryPasswordMinLength;

  /** Faux dès que l'opérateur a touché l'identifiant : on cesse de le suggérer. */
  private idEdited = false;

  protected readonly form = this.fb.nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(120)]],
    id: [
      '',
      [Validators.required, Validators.maxLength(householdIdMaxLength), Validators.pattern(householdIdPattern)],
    ],
  });

  protected readonly purgeForm = this.fb.nonNullable.group({ confirmId: '' });

  protected readonly adminForm = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    username: ['', [Validators.required, Validators.pattern(/^[a-zA-Z0-9][a-zA-Z0-9._-]{2,49}$/)]],
    temporaryPassword: ['', [Validators.required, Validators.minLength(temporaryPasswordMinLength)]],
  });

  constructor() {
    this.form.controls.displayName.valueChanges.pipe(takeUntilDestroyed()).subscribe((name) => {
      if (!this.idEdited) {
        this.form.controls.id.setValue(suggestHouseholdId(name));
      }
    });
  }

  ngOnInit(): void {
    void this.load();
  }

  protected markIdEdited(): void {
    this.idEdited = true;
  }

  protected fieldError(field: string): string | null {
    return this.problem()?.fields[field] ?? null;
  }

  protected async create(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const created = await this.run('creation', () => this.api.createHousehold(this.form.getRawValue()));
    if (created !== null) {
      this.form.reset();
      this.idEdited = false;
      await this.load();
      this.startAddAdmin(created);
      this.confirmation.set(
        `Le ménage « ${created.displayName} » est créé, avec un catalogue vide. Ajoutez maintenant la personne qui l'administrera.`,
      );
      // Le formulaire de création est en bas de page ; la confirmation et
      // la saisie de l'administrateur, en haut.
      window.scrollTo({ top: 0, behavior: 'smooth' });
    }
  }

  /** Rejoue la création à l'identique : elle reprend où elle s'était arrêtée. */
  protected async resume(household: Household): Promise<void> {
    const resumed = await this.run(household.id, () =>
      this.api.createHousehold({ id: household.id, displayName: household.displayName }),
    );
    if (resumed !== null) {
      await this.load();
      this.confirmation.set(`La création du ménage « ${resumed.displayName} » est terminée.`);
    }
  }

  protected startAddAdmin(household: Household): void {
    this.adminForm.reset();
    this.problem.set(null);
    this.purging.set(null);
    this.addingAdminTo.set(household.id);
  }

  protected archivedOn(household: Household): string | null {
    return household.archivedAt === null
      ? null
      : new Date(household.archivedAt).toLocaleDateString('fr-FR', { dateStyle: 'long' });
  }

  /** Archiver, ou reprendre un archivage interrompu. Réversible. */
  protected async archive(household: Household): Promise<void> {
    if (
      household.status === 'Active' &&
      !window.confirm(
        `Archiver le ménage « ${household.displayName} » ? Ses ${household.memberCount} compte(s) seront désactivés et leurs sessions fermées. Son catalogue est conservé : vous pourrez le restaurer.`,
      )
    ) {
      return;
    }

    const archived = await this.run(household.id, () => this.api.archiveHousehold(household.id));
    if (archived !== null) {
      await this.load();
      this.confirmation.set(
        `Le ménage « ${archived.displayName} » est archivé. Une session déjà ouverte peut encore servir quelques minutes, le temps que son jeton expire.`,
      );
    }
  }

  protected async restore(household: Household): Promise<void> {
    const restored = await this.run(household.id, () => this.api.restoreHousehold(household.id));
    if (restored !== null) {
      await this.load();
      this.confirmation.set(
        `Le ménage « ${restored.displayName} » est restauré. Les comptes que son administrateur avait lui-même désactivés le restent.`,
      );
    }
  }

  protected startPurge(household: Household): void {
    this.purgeForm.reset();
    this.problem.set(null);
    this.addingAdminTo.set(null);
    this.purging.set(household.id);
  }

  /** Suppression définitive, ou reprise d'une suppression interrompue. */
  protected async purge(household: Household): Promise<void> {
    const { confirmId } = this.purgeForm.getRawValue();
    if (confirmId.trim() !== household.id) {
      return;
    }

    // Réponse vide (204) : on la traduit en succès explicite.
    const done = await this.run(household.id, () =>
      this.api.purgeHousehold(household.id, confirmId.trim()).pipe(map(() => true)),
    );
    if (done !== null) {
      this.purging.set(null);
      await this.load();
      this.confirmation.set(
        `Le ménage « ${household.displayName} » est supprimé, avec son catalogue et ses comptes. L'identifiant « ${household.id} » est de nouveau libre.`,
      );
    }
  }

  protected async addAdmin(household: Household): Promise<void> {
    if (this.adminForm.invalid) {
      this.adminForm.markAllAsTouched();
      return;
    }

    const member = await this.run(household.id, () =>
      this.api.addHouseholdAdmin(household.id, this.adminForm.getRawValue()),
    );
    if (member !== null) {
      this.addingAdminTo.set(null);
      await this.load();
      this.confirmation.set(
        `« ${member.username} » administre désormais le ménage « ${household.displayName} ». Communiquez-lui son mot de passe temporaire : il devra en changer à la première connexion.`,
      );
    }
  }

  /** `null` en cas d'échec, pour que l'appelant ne poursuive pas. */
  private async run<T>(key: string, operation: () => Observable<T>): Promise<T | null> {
    this.busy.set(key);
    this.problem.set(null);
    this.confirmation.set(null);

    try {
      return await firstValueFrom(operation());
    } catch (error) {
      this.problem.set(describeHouseholdError(error));
      return null;
    } finally {
      this.busy.set(null);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.households.set(await firstValueFrom(this.api.households()));
    } catch (error) {
      this.problem.set(describeHouseholdError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
