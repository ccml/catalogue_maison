import { Component, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom, isObservable, type Observable } from 'rxjs';
import {
  describeMemberError,
  memberRoleLabels,
  orphanReasonLabels,
  temporaryPasswordMinLength,
  type Inconsistencies,
  type Member,
  type MemberProblem,
  type MemberRole,
  type OrphanEntry,
} from '../../core/api/member.models';
import { TenantsApi } from '../../core/api/tenants-api.service';

/**
 * Administration des membres du ménage, réservée à son administrateur.
 *
 * Ce qui est affiché vient de Tenants.Api, qui le lit dans Keycloak : rôles
 * et état du compte n'ont pas d'autre source. Un changement de place ne
 * prend effet qu'à la prochaine connexion de la personne — son jeton en
 * cours porte encore l'ancienne.
 */
@Component({
  selector: 'app-household-members',
  imports: [ReactiveFormsModule],
  templateUrl: './household-members.html',
  styleUrl: './household-members.scss',
})
export class HouseholdMembers implements OnInit {
  private readonly api = inject(TenantsApi);
  private readonly fb = inject(FormBuilder);

  protected readonly members = signal<readonly Member[]>([]);
  /** Écarts avec le registre ; `null` tant qu'ils n'ont pas été lus. */
  protected readonly inconsistencies = signal<Inconsistencies | null>(null);
  protected readonly loading = signal(true);
  protected readonly problem = signal<MemberProblem | null>(null);
  protected readonly confirmation = signal<string | null>(null);
  protected readonly busy = signal<string | null>(null);

  /** Membre dont on saisit un nouveau mot de passe, s'il y en a un. */
  protected readonly resetting = signal<string | null>(null);

  protected readonly roleLabels = memberRoleLabels;
  protected readonly orphanLabels = orphanReasonLabels;
  protected readonly minLength = temporaryPasswordMinLength;

  protected readonly form = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    username: ['', [Validators.required, Validators.pattern(/^[a-zA-Z0-9][a-zA-Z0-9._-]{2,49}$/)]],
    role: this.fb.nonNullable.control<MemberRole>('Child'),
    isAdmin: false,
    temporaryPassword: ['', [Validators.required, Validators.minLength(temporaryPasswordMinLength)]],
  });

  protected readonly passwordForm = this.fb.nonNullable.group({
    temporaryPassword: ['', [Validators.required, Validators.minLength(temporaryPasswordMinLength)]],
  });

  constructor() {
    // Seul un adulte administre : passer à « enfant » décoche la case.
    this.form.controls.role.valueChanges.pipe(takeUntilDestroyed()).subscribe((role) => {
      const isAdmin = this.form.controls.isAdmin;
      if (role === 'Child') {
        isAdmin.setValue(false);
        isAdmin.disable();
      } else {
        isAdmin.enable();
      }
    });
    this.form.controls.isAdmin.disable();
  }

  ngOnInit(): void {
    void this.load();
  }

  protected fullName(member: Member): string {
    const name = [member.firstName, member.lastName].filter((part) => part !== null).join(' ');
    return name.length > 0 ? name : member.username;
  }

  protected fieldError(field: string): string | null {
    return this.problem()?.fields[field] ?? null;
  }

  protected async create(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const created = await this.run('creation', () =>
      this.api.createMember({ ...value, isAdmin: value.role === 'Adult' && value.isAdmin }),
    );

    if (created !== null) {
      this.confirmation.set(
        `${this.fullName(created)} peut se connecter avec l'identifiant « ${created.username} » et le mot de passe que vous avez choisi. Il lui sera demandé d'en changer dès la première connexion.`,
      );
      this.form.reset({ role: 'Child', isAdmin: false });
      await this.load();
    }
  }

  protected async changeRole(member: Member, role: MemberRole, isAdmin: boolean): Promise<void> {
    const updated = await this.run(member.id, () =>
      this.api.updateRole(member.id, { role, isAdmin: role === 'Adult' && isAdmin }),
    );
    if (updated !== null) {
      this.replace(updated);
      this.confirmation.set(
        `La place de ${this.fullName(updated)} est enregistrée. Elle prendra effet à sa prochaine connexion.`,
      );
    }
  }

  protected startReset(member: Member): void {
    this.passwordForm.reset();
    this.problem.set(null);
    this.resetting.set(member.id);
  }

  protected async resetPassword(member: Member): Promise<void> {
    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      return;
    }

    const { temporaryPassword } = this.passwordForm.getRawValue();
    const done = await this.run(member.id, async () => {
      await firstValueFrom(this.api.resetPassword(member.id, temporaryPassword));
      return true;
    });

    if (done !== null) {
      this.resetting.set(null);
      this.confirmation.set(
        `Mot de passe de ${this.fullName(member)} remplacé. Il lui sera demandé d'en changer à sa prochaine connexion.`,
      );
    }
  }

  protected async toggleActive(member: Member): Promise<void> {
    if (
      member.enabled &&
      !window.confirm(
        `Désactiver le compte de ${this.fullName(member)} ? Ses sessions seront fermées, et il ne pourra plus se connecter tant que vous ne l'aurez pas réactivé.`,
      )
    ) {
      return;
    }

    const updated = await this.run(member.id, () =>
      member.enabled ? this.api.deactivate(member.id) : this.api.reactivate(member.id),
    );
    if (updated !== null) {
      this.replace(updated);
      this.confirmation.set(
        updated.enabled
          ? `Le compte de ${this.fullName(updated)} est réactivé.`
          : `Le compte de ${this.fullName(updated)} est désactivé.`,
      );
    }
  }

  protected hasInconsistencies(report: Inconsistencies): boolean {
    return report.unregisteredAccounts.length > 0 || report.orphanEntries.length > 0;
  }

  protected async register(member: Member): Promise<void> {
    const registered = await this.run(member.id, () => this.api.register(member.id));
    if (registered !== null) {
      this.confirmation.set(`${this.fullName(registered)} est inscrit au registre du ménage.`);
      await this.load();
    }
  }

  protected async removeEntry(entry: OrphanEntry): Promise<void> {
    if (
      !window.confirm(
        `Retirer « ${entry.username} » du registre du ménage ? Aucun compte de connexion n'est modifié.`,
      )
    ) {
      return;
    }

    const done = await this.run(entry.subject, async () => {
      await firstValueFrom(this.api.removeRegistryEntry(entry.subject));
      return true;
    });
    if (done !== null) {
      this.confirmation.set(`« ${entry.username} » est retiré du registre du ménage.`);
      await this.load();
    }
  }

  private replace(updated: Member): void {
    this.members.update((members) => members.map((m) => (m.id === updated.id ? updated : m)));
  }

  /**
   * Exécute une opération en signalant son refus. `null` en cas d'échec,
   * pour que l'appelant ne poursuive pas.
   */
  private async run<T>(key: string, operation: () => Promise<T> | Observable<T>): Promise<T | null> {
    this.busy.set(key);
    this.problem.set(null);
    this.confirmation.set(null);

    try {
      const result = operation();
      return isObservable(result) ? await firstValueFrom(result) : await result;
    } catch (error) {
      this.problem.set(describeMemberError(error));
      return null;
    } finally {
      this.busy.set(null);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const [members, inconsistencies] = await Promise.all([
        firstValueFrom(this.api.members()),
        firstValueFrom(this.api.inconsistencies()),
      ]);
      this.members.set(members);
      this.inconsistencies.set(inconsistencies);
    } catch (error) {
      this.problem.set(describeMemberError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
