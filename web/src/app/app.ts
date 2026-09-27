import { DOCUMENT, Component, DestroyRef, computed, effect, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, startWith } from 'rxjs';
import { MetadataService } from './core/services/metadata.service';
import { AuthService } from './core/services/auth.service';
import { MessagesService } from './core/services/messages.service';

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
  private readonly messages = inject(MessagesService);

  protected readonly roleCount = this.metadata.roles;
  protected readonly user = this.auth.user;
  protected readonly isSignedIn = this.auth.isSignedIn;
  protected readonly isAdmin = this.auth.isAdmin;
  protected readonly canWrite = this.auth.canWrite;

  /**
   * The unread badge.
   *
   * Polled rather than pushed: the socket only carries threads you have open, so a
   * message arriving in a channel you are not looking at would never reach the badge.
   * Sixty seconds is slow enough to be invisible in the network tab and fast enough that
   * the number is not embarrassingly stale when somebody glances at it.
   */
  protected readonly unread = this.messages.unread;

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

  constructor() {
    // Refresh the badge on sign-in and on every navigation: moving between screens is
    // exactly when somebody looks at it, and it costs one small request.
    effect(() => {
      this.url();
      if (this.isSignedIn()) void this.messages.refreshUnread();
    });

    const timer = setInterval(() => {
      if (this.isSignedIn()) void this.messages.refreshUnread();
    }, 60_000);

    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

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
