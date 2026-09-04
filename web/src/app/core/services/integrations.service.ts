import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';

/**
 * What the server will tell us about the Jira connection.
 *
 * Note what is missing: the API token. The server returns only `tokenHint` (a mask plus
 * the last four characters), so the secret never reaches a browser, a devtools network
 * tab, or a screenshot of this screen.
 */
export interface JiraSettingsView {
  configured: boolean;
  enabled: boolean;
  baseUrl: string;
  email: string;
  tokenHint: string | null;
  autoApply: boolean;
  syncIntervalMinutes: number;
  updatedBy: string | null;
  updatedAt: string | null;
  lastSyncAt: string | null;
  lastSyncResult: string | null;
  /** True when environment configuration pins the credentials and the UI cannot change them. */
  overriddenByConfiguration: boolean;
}

export interface SaveJiraSettingsRequest {
  baseUrl: string;
  email: string;
  /** Blank means "keep the token already stored" — the UI cannot read it back to resend it. */
  apiToken: string | null;
  enabled: boolean;
  autoApply: boolean;
  syncIntervalMinutes: number;
}

export interface JiraConnectionResult {
  enabled: boolean;
  reachable: boolean;
  message: string;
  probedProjectKey: string | null;
  issuesSeen: number | null;
}

export interface JiraSyncReport {
  ran: boolean;
  message: string;
  boardsConsidered: number;
  boardsUpdated: number;
  boardsUnreachable: number;
  details: string[];
}

@Injectable({ providedIn: 'root' })
export class IntegrationsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get jiraUrl(): string {
    return `${this.baseUrl}/integrations/jira`;
  }

  get(): Promise<JiraSettingsView> {
    return firstValueFrom(this.http.get<JiraSettingsView>(this.jiraUrl));
  }

  save(request: SaveJiraSettingsRequest): Promise<JiraSettingsView> {
    return firstValueFrom(this.http.put<JiraSettingsView>(this.jiraUrl, request));
  }

  clear(): Promise<void> {
    return firstValueFrom(this.http.delete<void>(this.jiraUrl));
  }

  test(projectKey: string | null): Promise<JiraConnectionResult> {
    return firstValueFrom(
      this.http.post<JiraConnectionResult>(`${this.jiraUrl}/test`, { projectKey }),
    );
  }

  syncNow(): Promise<JiraSyncReport> {
    return firstValueFrom(this.http.post<JiraSyncReport>(`${this.jiraUrl}/sync`, {}));
  }
}
