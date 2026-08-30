import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, firstValueFrom, tap } from 'rxjs';
import { API_BASE_URL } from '../api.config';

export type UserRoleName = 'Viewer' | 'ProductOwner' | 'Admin';

export interface SignedInUser {
  id: string;
  email: string;
  displayName: string;
  role: number;
  roleName: UserRoleName;
}

export interface AuthResult {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: SignedInUser;
}

const ACCESS_KEY = 'ssb.access';
const REFRESH_KEY = 'ssb.refresh';

/**
 * Session state and token handling.
 *
 * Tokens live in localStorage so a refresh does not sign the user out. That is a
 * deliberate trade: it is vulnerable to XSS in a way an httpOnly cookie is not, and the
 * mitigation is that access tokens are short-lived and refresh tokens rotate on every
 * use, so a stolen pair has a narrow window. A cookie-based scheme would be the upgrade
 * if this ever leaves the corporate network.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly router = inject(Router);

  private readonly userSignal = signal<SignedInUser | null>(null);

  readonly user = this.userSignal.asReadonly();
  readonly isSignedIn = computed(() => this.userSignal() !== null);
  readonly role = computed<UserRoleName | null>(() => this.userSignal()?.roleName ?? null);

  /** Viewers get a read-only app; the UI hides what the server would refuse anyway. */
  readonly canWrite = computed(() => {
    const role = this.role();
    return role === 'Admin' || role === 'ProductOwner';
  });

  readonly isAdmin = computed(() => this.role() === 'Admin');

  get accessToken(): string | null {
    return safeRead(ACCESS_KEY);
  }

  get refreshToken(): string | null {
    return safeRead(REFRESH_KEY);
  }

  login(email: string, password: string): Observable<AuthResult> {
    return this.http
      .post<AuthResult>(`${this.baseUrl}/auth/login`, { email, password })
      .pipe(tap((result) => this.apply(result)));
  }

  /**
   * Restores a session on app start. Tries the stored access token first, then the
   * refresh token, then gives up quietly — a signed-out user is a normal state, not
   * an error worth surfacing.
   */
  async restore(): Promise<void> {
    if (!this.accessToken && !this.refreshToken) return;

    try {
      const user = await firstValueFrom(this.http.get<SignedInUser>(`${this.baseUrl}/auth/me`));
      this.userSignal.set(user);
      return;
    } catch {
      // Access token expired or missing — fall through to refresh.
    }

    try {
      await this.refresh();
    } catch {
      this.clear();
    }
  }

  async refresh(): Promise<AuthResult> {
    const refreshToken = this.refreshToken;
    if (!refreshToken) {
      throw new Error('No refresh token');
    }

    const result = await firstValueFrom(
      this.http.post<AuthResult>(`${this.baseUrl}/auth/refresh`, { refreshToken }),
    );

    this.apply(result);
    return result;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${this.baseUrl}/auth/logout`, {}));
    } catch {
      // Revoking server-side is best-effort; the local session goes either way.
    }

    this.clear();
    void this.router.navigate(['/login']);
  }

  /** Clears local state without a server round trip — used when a refresh fails. */
  clear(): void {
    this.userSignal.set(null);
    safeRemove(ACCESS_KEY);
    safeRemove(REFRESH_KEY);
  }

  private apply(result: AuthResult): void {
    safeWrite(ACCESS_KEY, result.accessToken);
    safeWrite(REFRESH_KEY, result.refreshToken);
    this.userSignal.set(result.user);
  }
}

// localStorage throws in private windows and when site data is blocked, so every
// access is guarded rather than allowed to break the app.
function safeRead(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function safeWrite(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Session simply will not survive a reload; not worth failing the login over.
  }
}

function safeRemove(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // Nothing useful to do.
  }
}
