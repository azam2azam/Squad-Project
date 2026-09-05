import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';

/**
 * Accounts that can sign in — distinct from the roster, which is who appears on slides.
 * Most roster members never log in, and an admin need not be on any squad.
 *
 * No password ever comes back from the server; `hasPassword` only says whether one is set.
 */
export interface AppUser {
  id: string;
  email: string;
  displayName: string;
  role: number;
  roleLabel: string;
  isActive: boolean;
  hasPassword: boolean;
  personId: string | null;
  personName: string | null;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface AccessLevel {
  value: number;
  name: string;
  label: string;
  description: string;
}

export interface CreateUserRequest {
  email: string;
  displayName: string;
  role: number;
  password: string;
  personId: string | null;
}

export interface UpdateUserRequest {
  displayName: string;
  role: number;
  personId: string | null;
}

interface Paged<T> {
  items: T[];
  totalCount: number;
}

/** Mirrors the server rule, so the form does not accept what the API will refuse. */
export const MIN_PASSWORD_LENGTH = 12;

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/users`;
  }

  async list(includeInactive: boolean, search?: string): Promise<AppUser[]> {
    const params: Record<string, string> = {
      includeInactive: String(includeInactive),
      pageSize: '200',
    };
    if (search?.trim()) params['q'] = search.trim();

    const page = await firstValueFrom(this.http.get<Paged<AppUser>>(this.url, { params }));
    return page.items;
  }

  accessLevels(): Promise<AccessLevel[]> {
    return firstValueFrom(this.http.get<AccessLevel[]>(`${this.url}/roles`));
  }

  create(request: CreateUserRequest): Promise<AppUser> {
    return firstValueFrom(this.http.post<AppUser>(this.url, request));
  }

  update(id: string, request: UpdateUserRequest): Promise<AppUser> {
    return firstValueFrom(this.http.put<AppUser>(`${this.url}/${id}`, request));
  }

  setActive(id: string, isActive: boolean): Promise<AppUser> {
    return firstValueFrom(this.http.put<AppUser>(`${this.url}/${id}/active`, { isActive }));
  }

  resetPassword(id: string, newPassword: string): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.url}/${id}/password`, { newPassword }));
  }

  changeOwnPassword(currentPassword: string, newPassword: string): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(`${this.url}/me/password`, { currentPassword, newPassword }),
    );
  }
}
