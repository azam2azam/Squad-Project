import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';

/**
 * A programme above the boards — "VIDA 4", "AI", and so on.
 *
 * Distinct from a board's Product, which names the module it covers (Discharge, Invoice).
 * A category collects many products under one programme.
 */
export interface BoardCategory {
  id: string;
  name: string;
  description: string | null;
  color: string;
  orderIndex: number;
  isActive: boolean;
  /** How many boards sit in it, so retiring one is an informed decision. */
  boardCount: number;
}

export interface CreateCategoryRequest {
  name: string;
  description: string | null;
  color: string;
}

export interface UpdateCategoryRequest {
  name: string;
  description: string | null;
  color: string;
  orderIndex: number;
}

@Injectable({ providedIn: 'root' })
export class CategoriesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/categories`;
  }

  list(includeInactive = false): Promise<BoardCategory[]> {
    return firstValueFrom(
      this.http.get<BoardCategory[]>(this.url, {
        params: { includeInactive: String(includeInactive) },
      }),
    );
  }

  create(request: CreateCategoryRequest): Promise<BoardCategory> {
    return firstValueFrom(this.http.post<BoardCategory>(this.url, request));
  }

  update(id: string, request: UpdateCategoryRequest): Promise<BoardCategory> {
    return firstValueFrom(this.http.put<BoardCategory>(`${this.url}/${id}`, request));
  }

  setActive(id: string, isActive: boolean): Promise<BoardCategory> {
    return firstValueFrom(this.http.put<BoardCategory>(`${this.url}/${id}/active`, { isActive }));
  }

  /** Moves many boards at once. A null categoryId takes them out of every programme. */
  assign(boardIds: string[], categoryId: string | null): Promise<{ moved: number }> {
    return firstValueFrom(
      this.http.post<{ moved: number }>(`${this.url}/assign`, { boardIds, categoryId }),
    );
  }
}
