import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { Icon } from './shared/icon';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
}
