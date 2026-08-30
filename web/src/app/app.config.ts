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
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),

    // Restores the session before the first route resolves, so a signed-in user who
    // reloads is not bounced to the login page. Reference data follows, since the API
    // now requires a token for it.
    provideAppInitializer(async () => {
      await inject(AuthService).restore();

      try {
        await inject(MetadataService).load();
      } catch {
        // Signed-out users cannot read metadata; the login page does not need it,
        // and it loads on demand once they are in.
      }
    }),
  ],
};
