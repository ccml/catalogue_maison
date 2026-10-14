import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-sign-in',
  templateUrl: './sign-in.html',
  styleUrl: './sign-in.scss',
})
export class SignIn {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  protected readonly busy = signal(false);

  protected async signIn(): Promise<void> {
    this.busy.set(true);
    const suite = this.route.snapshot.queryParamMap.get('suite');
    await this.auth.signIn(suite ?? '/catalogue');
  }
}
