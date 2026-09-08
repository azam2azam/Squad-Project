import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';

/**
 * The Smartsheet connection, as an admin may see it.
 *
 * As with Jira, the access token is absent by design: the server returns only
 * `tokenHint` (a mask plus the last four characters), so the secret never reaches a
 * browser, a devtools network tab, or a screenshot of this screen.
 */
export interface SmartsheetSettingsView {
  configured: boolean;
  enabled: boolean;
  baseUrl: string;
  tokenHint: string | null;
  autoApply: boolean;
  syncIntervalMinutes: number;
  /** A sheet has no fixed shape, so the sync is told which columns to read. */
  progressColumn: string;
  statusColumn: string;
  updatedBy: string | null;
  updatedAt: string | null;
  lastSyncAt: string | null;
  lastSyncResult: string | null;
  overriddenByConfiguration: boolean;
}

export interface SaveSmartsheetRequest {
  baseUrl: string;
  /** Blank means "keep the token already stored" — the UI cannot read it back to resend. */
  accessToken: string | null;
  enabled: boolean;
  autoApply: boolean;
  syncIntervalMinutes: number;
  progressColumn: string | null;
  statusColumn: string | null;
}

export interface SmartsheetConnectionResult {
  enabled: boolean;
  reachable: boolean;
  message: string;
  probedSheetId: string | null;
  rowsSeen: number | null;
}

export interface SmartsheetSyncReport {
  ran: boolean;
  message: string;
  boardsConsidered: number;
  boardsUpdated: number;
  boardsUnreachable: number;
  details: string[];
}

@Injectable({ providedIn: 'root' })
export class SmartsheetService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/integrations/smartsheet`;
  }

  get(): Promise<SmartsheetSettingsView> {
    return firstValueFrom(this.http.get<SmartsheetSettingsView>(this.url));
  }

  save(request: SaveSmartsheetRequest): Promise<SmartsheetSettingsView> {
    return firstValueFrom(this.http.put<SmartsheetSettingsView>(this.url, request));
  }

  clear(): Promise<void> {
    return firstValueFrom(this.http.delete<void>(this.url));
  }

  test(sheetId: string | null): Promise<SmartsheetConnectionResult> {
    return firstValueFrom(
      this.http.post<SmartsheetConnectionResult>(`${this.url}/test`, { sheetId }),
    );
  }

  syncNow(): Promise<SmartsheetSyncReport> {
    return firstValueFrom(this.http.post<SmartsheetSyncReport>(`${this.url}/sync`, {}));
  }
}
