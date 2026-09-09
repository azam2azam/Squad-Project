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
  /** Delivery risk, tracked separately from status. */
  riskLevel?: number;
  riskNote?: string | null;
  velocity?: number | null;
  targetDate?: string | null;
  jiraProjectKey?: string | null;
  jiraBoardId?: string | null;
  /** The programme this board belongs to. Null takes it out of every category. */
  categoryId?: string | null;
  /** The Smartsheet sheet this board tracks. Null unlinks it. */
  smartsheetSheetId?: string | null;
  /** The short handle people type in a Telegram update. Null removes it. */
  code?: string | null;
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

  audit(id: string): Observable<BoardAuditEntry[]> {
    return this.http.get<BoardAuditEntry[]>(`${this.baseUrl}/boards/${id}/audit`);
  }

  /** Pulls a Jira suggestion. Read-only: it never writes to the board. */
  jiraSync(id: string): Observable<JiraSuggestion> {
    return this.http.post<JiraSuggestion>(`${this.baseUrl}/boards/${id}/jira/sync`, {});
  }

  /** Same contract as jiraSync: returns a suggestion, never writes to the board. */
  smartsheetSync(id: string): Observable<SmartsheetSuggestion> {
    return this.http.post<SmartsheetSuggestion>(
      `${this.baseUrl}/boards/${id}/smartsheet/sync`,
      {},
    );
  }

  /** Bulk restore from an exported file. Upserts by id, so re-importing is a no-op. */
  import(file: unknown): Observable<ImportResult> {
    return this.http.post<ImportResult>(`${this.baseUrl}/import`, file);
  }
}

/** What a bulk import actually changed, so the UI can report it rather than guess. */
export interface ImportResult {
  peopleCreated: number;
  peopleUpdated: number;
  boardsCreated: number;
  boardsUpdated: number;
  membersLinked: number;
  warnings: string[];
}

/** One line of a board's change log. */
export interface BoardAuditEntry {
  id: string;
  field: string;
  oldValue: string | null;
  newValue: string | null;
  changedBy: string;
  changedAt: string;
  summary: string;
}

/** A Jira-derived suggestion. Never applied automatically — the PO accepts it. */
export interface JiraSuggestion {
  available: boolean;
  reason: string | null;
  sprintName: string | null;
  doneIssues: number;
  totalIssues: number;
  blockedIssues: number;
  suggestedProgressPercent: number;
  suggestedStatus: number;
  suggestedStatusLabel: string;
  suggestedStatusColor: string;
  rationale: string;
  currentSprint: string | null;
  currentProgressPercent: number;
  currentStatus: number;
}

/**
 * What a sheet suggests for a board.
 *
 * `progressFromColumn` distinguishes a percentage averaged from a real "% Complete"
 * column from one inferred by counting finished rows — different levels of confidence,
 * and the panel says which so a PO can judge it.
 */
export interface SmartsheetSuggestion {
  available: boolean;
  reason: string | null;
  sheetName: string | null;
  doneRows: number;
  totalRows: number;
  blockedRows: number;
  suggestedProgressPercent: number;
  progressFromColumn: boolean;
  suggestedStatus: number;
  suggestedStatusLabel: string;
  suggestedStatusColor: string;
  rationale: string;
  currentProgressPercent: number;
  currentStatus: number;
}
