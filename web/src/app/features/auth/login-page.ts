import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { MetadataService } from '../../core/services/metadata.service';

/**
 * Local sign-in.
 *
 * Only the seeded administrator is offered here. The demo squads and their Product Owner
 * and Viewer logins were removed when the app was cleared for real use, so listing them
 * would hand someone credentials that fail.
 */
@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login-page.html',
  styleUrl: './login-page.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly metadata = inject(MetadataService);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly demoAccounts = [
    { email: 'admin@pirt.example', role: 'Admin', can: 'Everything' },
  ];

  protected readonly demoPassword = 'Demo!Pass123';

  protected useDemo(email: string): void {
    this.email.set(email);
    this.password.set(this.demoPassword);
    this.error.set(null);
  }

  protected submit(): void {
    if (this.busy()) return;

    const email = this.email().trim();
    const password = this.password();
    if (!email || !password) return;

    this.busy.set(true);
    this.error.set(null);

    this.auth.login(email, password).subscribe({
      next: async () => {
        // Reference data could not be fetched while signed out, so load it now
        // before the first slide renders and members appear without colours.
        try {
          await this.metadata.load();
        } catch {
          // Non-fatal: the app falls back to server-supplied labels per board.
        }

        this.busy.set(false);
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/portfolio';
        void this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(
          err?.status === 401
            ? 'Email or password is incorrect.'
            : 'Could not sign in. Is the API running?',
        );
      },
    });
  }
}
