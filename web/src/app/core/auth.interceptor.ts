import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './services/auth.service';

/** Endpoints that must not carry a token or trigger a refresh loop. */
const ANONYMOUS = ['/auth/login', '/auth/refresh'];

/**
 * Attaches the bearer token, and on a 401 tries exactly one refresh-and-retry before
 * giving up and sending the user to the login page.
 *
 * The single-retry limit matters: without it an expired refresh token produces an
 * infinite 401 → refresh → 401 loop.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isAnonymous = ANONYMOUS.some((path) => req.url.includes(path));
  const token = auth.accessToken;

  const authorised =
    token && !isAnonymous ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authorised).pipe(
    catchError((error: unknown) => {
      const is401 = error instanceof HttpErrorResponse && error.status === 401;

      if (!is401 || isAnonymous || !auth.refreshToken) {
        return throwError(() => error);
      }

      return from(auth.refresh()).pipe(
        switchMap((result) =>
          next(req.clone({ setHeaders: { Authorization: `Bearer ${result.accessToken}` } })),
        ),
        catchError((refreshError: unknown) => {
          auth.clear();
          void router.navigate(['/login']);
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};
