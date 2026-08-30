import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api.config';
import type { BoardDetail, BoardStatus, BoardSummary, PagedResult } from '../models/board.models';

/** Payload for creating a board. */
export interface CreateBoardRequest {
  title: string;
  product: string;
  squadName: string;
  sprint: string | null;
  status: BoardStatus;
  progressPercent: number;
}

/** Payload for updating board metadata. Sent whole, not patched. */
export interface UpdateBoardRequest extends CreateBoardRequest {
  blockerNote?: string | null;
  velocity?: number | null;
  targetDate?: string | null;
  jiraProjectKey?: string | null;
  jiraBoardId?: string | null;
}

@Injectable({ providedIn: 'root' })
export class BoardsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(options: { q?: string; status?: BoardStatus; page?: number; pageSize?: number } = {}) {
    let params = new HttpParams();
    if (options.q) params = params.set('q', options.q);
    if (options.status !== undefined) params = params.set('status', options.status);
    if (options.page) params = params.set('page', options.page);
    if (options.pageSize) params = params.set('pageSize', options.pageSize);

    return this.http.get<PagedResult<BoardSummary>>(`${this.baseUrl}/boards`, { params });
  }

  get(id: string): Observable<BoardDetail> {
    return this.http.get<BoardDetail>(`${this.baseUrl}/boards/${id}`);
  }

  create(request: CreateBoardRequest): Observable<BoardDetail> {
    return this.http.post<BoardDetail>(`${this.baseUrl}/boards`, request);
  }

  update(id: string, request: UpdateBoardRequest): Observable<BoardDetail> {
    return this.http.put<BoardDetail>(`${this.baseUrl}/boards/${id}`, { ...request, id });
  }

  duplicate(id: string, newTitle?: string): Observable<BoardDetail> {
    return this.http.post<BoardDetail>(`${this.baseUrl}/boards/${id}/duplicate`, {
      newTitle: newTitle ?? null,
    });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/boards/${id}`);
  }

  reorder(items: { id: string; orderIndex: number }[]): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/boards/reorder`, items);
  }
}
