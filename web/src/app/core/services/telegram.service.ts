import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';

/**
 * The Telegram connection, as an admin may see it.
 *
 * As with Jira and Smartsheet, the bot token is absent by design: the server returns only
 * `tokenHint` (a mask plus the last four characters), so the secret never reaches a
 * browser, a devtools network tab, or a screenshot of this screen.
 */
export interface TelegramSettingsView {
  configured: boolean;
  enabled: boolean;
  baseUrl: string;
  tokenHint: string | null;
  /** Filled in by Test connection — the @name people message. */
  botUsername: string | null;
  allowedChatIds: string | null;
  replyToUnknownSenders: boolean;
  lastUpdateId: number;
  updatedBy: string | null;
  updatedAt: string | null;
  lastPollAt: string | null;
  lastPollResult: string | null;
  overriddenByConfiguration: boolean;
}

export interface SaveTelegramRequest {
  baseUrl: string;
  /** Blank means "keep the token already stored" — the UI cannot read it back to resend. */
  botToken: string | null;
  enabled: boolean;
  allowedChatIds: string | null;
  replyToUnknownSenders: boolean;
}

export interface TelegramConnectionResult {
  connected: boolean;
  username: string | null;
  name: string | null;
  message: string;
}

export interface TelegramPollReport {
  read: number;
  applied: number;
  refused: number;
  message: string;
}

export interface TelegramSimulation {
  reply: string;
  outcome: string;
  outcomeLabel: string;
  detail: string;
  dryRun: boolean;
}

export interface TelegramEnrolment {
  code: string;
  userDisplayName: string;
  expiresAt: string;
  instructions: string;
}

export interface TelegramLink {
  id: string;
  telegramUserId: number;
  telegramUsername: string | null;
  displayName: string;
  userId: string;
  userDisplayName: string;
  userRole: string;
  isActive: boolean;
  linkedAt: string;
  lastSeenAt: string | null;
}

export interface TelegramMessage {
  id: string;
  receivedAt: string;
  senderName: string;
  text: string;
  outcome: string;
  outcomeLabel: string;
  succeeded: boolean;
  detail: string | null;
  boardId: string | null;
  boardTitle: string | null;
}

export interface BoardCode {
  id: string;
  title: string;
  code: string | null;
  assigned: boolean;
}

@Injectable({ providedIn: 'root' })
export class TelegramService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/integrations/telegram`;
  }

  get(): Promise<TelegramSettingsView> {
    return firstValueFrom(this.http.get<TelegramSettingsView>(this.url));
  }

  save(request: SaveTelegramRequest): Promise<TelegramSettingsView> {
    return firstValueFrom(this.http.put<TelegramSettingsView>(this.url, request));
  }

  clear(): Promise<void> {
    return firstValueFrom(this.http.delete<void>(this.url));
  }

  test(): Promise<TelegramConnectionResult> {
    return firstValueFrom(this.http.post<TelegramConnectionResult>(`${this.url}/test`, {}));
  }

  pollNow(): Promise<TelegramPollReport> {
    return firstValueFrom(this.http.post<TelegramPollReport>(`${this.url}/poll`, {}));
  }

  simulate(text: string, dryRun: boolean): Promise<TelegramSimulation> {
    return firstValueFrom(
      this.http.post<TelegramSimulation>(`${this.url}/simulate`, { text, dryRun }),
    );
  }

  createEnrolment(userId: string | null): Promise<TelegramEnrolment> {
    return firstValueFrom(this.http.post<TelegramEnrolment>(`${this.url}/enrolments`, { userId }));
  }

  links(): Promise<TelegramLink[]> {
    return firstValueFrom(this.http.get<TelegramLink[]>(`${this.url}/links`));
  }

  revokeLink(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.url}/links/${id}`));
  }

  messages(take = 40): Promise<TelegramMessage[]> {
    return firstValueFrom(
      this.http.get<TelegramMessage[]>(`${this.url}/messages`, { params: { take } }),
    );
  }

  assignBoardCodes(): Promise<BoardCode[]> {
    return firstValueFrom(this.http.post<BoardCode[]>(`${this.url}/board-codes`, {}));
  }

  template(): Promise<{ template: string }> {
    return firstValueFrom(this.http.get<{ template: string }>(`${this.url}/template`));
  }
}
