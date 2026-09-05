import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';

/**
 * The roles a squad member can hold — the values behind "Default role".
 *
 * The seven built-ins can be renamed and recoloured but not removed: they are the values
 * every board written before roles were configurable already uses.
 */
export interface SquadRole {
  value: number;
  /** Stable identifier used by imports and the API, e.g. "ScrumMaster". */
  name: string;
  label: string;
  pluralLabel: string;
  color: string;
  orderIndex: number;
  isBuiltIn: boolean;
  isActive: boolean;
  /** How many people hold this role, so retiring it is an informed decision. */
  peopleUsing: number;
}

export interface CreateRoleRequest {
  name: string;
  label: string;
  pluralLabel: string | null;
  color: string;
}

export interface UpdateRoleRequest {
  label: string;
  pluralLabel: string | null;
  color: string;
  orderIndex: number;
}

@Injectable({ providedIn: 'root' })
export class RolesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/roles`;
  }

  list(includeInactive = false): Promise<SquadRole[]> {
    return firstValueFrom(
      this.http.get<SquadRole[]>(this.url, { params: { includeInactive: String(includeInactive) } }),
    );
  }

  create(request: CreateRoleRequest): Promise<SquadRole> {
    return firstValueFrom(this.http.post<SquadRole>(this.url, request));
  }

  update(value: number, request: UpdateRoleRequest): Promise<SquadRole> {
    return firstValueFrom(this.http.put<SquadRole>(`${this.url}/${value}`, request));
  }

  setActive(value: number, isActive: boolean): Promise<SquadRole> {
    return firstValueFrom(this.http.put<SquadRole>(`${this.url}/${value}/active`, { isActive }));
  }
}
