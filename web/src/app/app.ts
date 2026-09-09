import { DOCUMENT, Component, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, startWith } from 'rxjs';
import { MetadataService } from './core/services/metadata.service';
import { AuthService } from './core/services/auth.service';

/**
 * Application shell: the persistent header and the routed outlet.
 *
 * Present mode, the export slide route and the login page render chromeless — the header
 * must not appear in a screenshot of the slide, and it would be meaningless before
 * sign-in.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly metadata = inject(MetadataService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);

  protected readonly roleCount = this.metadata.roles;
  protected readonly user = this.auth.user;
  protected readonly isSignedIn = this.auth.isSignedIn;
  protected readonly isAdmin = this.auth.isAdmin;
  protected readonly canWrite = this.auth.canWrite;

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** Routes that own the whole viewport and must not show app chrome. */
  protected readonly chromeless = computed(() => {
    const url = this.url();
    return url.startsWith('/slide') || url.startsWith('/present') || url.startsWith('/login');
  });

  protected signOut(): void {
    void this.auth.logout();
  }

  /**
   * The skip link cannot be a plain `href="#main"`: with `<base href="/">` a fragment-only
   * href resolves to `/#main`, which routes to the dashboard instead of skipping the nav.
   * Moving focus is the point anyway — scrolling alone leaves the next Tab back in the rail.
   */
  protected skipToMain(event: Event): void {
    event.preventDefault();

    const main = this.document.getElementById('main');
    main?.focus();
    main?.scrollIntoView();
  }
}
