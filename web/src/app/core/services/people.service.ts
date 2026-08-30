import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api.config';
import type { PagedResult, Person, Role, SquadMember } from '../models/board.models';

export interface PersonRequest {
  fullName: string;
  defaultRole: Role;
  defaultDetail: string | null;
  email: string | null;
  avatarColorOverride: string | null;
}

/** Add a member either by roster id or by quick-creating the person inline. */
export interface AddMemberRequest {
  personId?: string | null;
  newPerson?: {
    fullName: string;
    defaultRole: Role;
    defaultDetail: string | null;
    email: string | null;
  } | null;
  role: Role;
  detail: string | null;
  allocationPercent: number | null;
}

@Injectable({ providedIn: 'root' })
export class PeopleService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(options: { q?: string; includeInactive?: boolean; pageSize?: number } = {}) {
    let params = new HttpParams();
    if (options.q) params = params.set('q', options.q);
    if (options.includeInactive) params = params.set('includeInactive', true);
    if (options.pageSize) params = params.set('pageSize', options.pageSize);

    return this.http.get<PagedResult<Person>>(`${this.baseUrl}/people`, { params });
  }

  create(request: PersonRequest): Observable<Person> {
    return this.http.post<Person>(`${this.baseUrl}/people`, request);
  }

  update(id: string, request: PersonRequest): Observable<Person> {
    return this.http.put<Person>(`${this.baseUrl}/people/${id}`, { ...request, id });
  }

  /** Soft delete — squad history is preserved. */
  deactivate(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/people/${id}`);
  }

  reactivate(id: string): Observable<Person> {
    return this.http.post<Person>(`${this.baseUrl}/people/${id}/reactivate`, {});
  }

  // ---- membership ----

  addMember(boardId: string, request: AddMemberRequest): Observable<SquadMember> {
    return this.http.post<SquadMember>(`${this.baseUrl}/boards/${boardId}/members`, request);
  }

  updateMember(
    id: string,
    request: { role: Role; detail: string | null; allocationPercent: number | null },
  ): Observable<SquadMember> {
    return this.http.put<SquadMember>(`${this.baseUrl}/members/${id}`, request);
  }

  removeMember(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/members/${id}`);
  }

  reorderMembers(boardId: string, orderedMemberIds: string[]): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl}/boards/${boardId}/members/reorder`,
      orderedMemberIds,
    );
  }
}
