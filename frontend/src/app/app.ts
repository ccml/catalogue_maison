import { Component, ElementRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthService } from './core/auth/auth.service';
import { initialsOf, isSharingSection } from './masthead';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  host: {
    '(document:click)': 'closeAccountOnOutsideClick($event)',
    '(document:keydown.escape)': 'accountOpen.set(false)',
  },
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly inSharing = computed(() => isSharingSection(this.url()));
  protected readonly initials = computed(() => initialsOf(this.auth.displayName()));
  protected readonly hasHouseholdLinks = computed(
    () => this.auth.mayWrite() || this.auth.mayAdministerHousehold(),
  );

  /** Menu du compte : seul chemin vers le ménage et la déconnexion sur téléphone. */
  protected readonly accountOpen = signal(false);

  constructor() {
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.accountOpen.set(false));
  }

  protected closeAccountOnOutsideClick(event: MouseEvent): void {
    const account = this.host.nativeElement.querySelector('.account');
    if (account && !account.contains(event.target as Node)) {
      this.accountOpen.set(false);
    }
  }
}
