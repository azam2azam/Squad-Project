import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './services/auth.service';

/**
 * Route guards mirroring the API policies (spec section 8). These are a convenience,
 * not the enforcement: the server refuses the same requests regardless, so a user who
 * bypasses the router gains nothing.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isSignedIn()) return true;

  // Remembered so the user lands where they were headed after signing in.
  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: state.url },
  });
};

/** Admin-only routes, such as roster management. */
export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isSignedIn()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }

  return auth.isAdmin() ? true : router.createUrlTree(['/portfolio']);
};
