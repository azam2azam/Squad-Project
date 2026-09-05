import {
  ApplicationConfig,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
  inject,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withInMemoryScrolling } from '@angular/router';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth.interceptor';
import { AuthService } from './core/services/auth.service';
import { MetadataService } from './core/services/metadata.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // anchorScrolling lets the guide's contents links deep-link into a section — without
    // it the browser looks for the fragment before the lazy component has rendered and
    // silently lands at the top of the page.
    provideRouter(
      routes,
      withInMemoryScrolling({ scrollPositionRestoration: 'top', anchorScrolling: 'enabled' }),
    ),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),

    // Restores the session before the first route resolves, so a signed-in user who
    // reloads is not bounced to the login page. Reference data follows, since the API
    // now requires a token for it.
    provideAppInitializer(async () => {
      // Both services are resolved here, before the first await: inject() only works
      // inside an injection context, and awaiting leaves it. Resolving MetadataService
      // after the await threw on every page load, which left roles and statuses empty
      // everywhere — the roster could not add a person because the role list was blank.
      const auth = inject(AuthService);
      const metadata = inject(MetadataService);

      await auth.restore();

      try {
        await metadata.load();
      } catch (error) {
        // A signed-out visitor cannot read metadata, and the login page does not need
        // it — that case is expected and silent. Anything else is logged rather than
        // swallowed, because a silent failure here empties every dropdown in the app.
        if (auth.isSignedIn()) {
          console.error('Could not load reference data (roles and statuses).', error);
        }
      }
    }),
  ],
};
