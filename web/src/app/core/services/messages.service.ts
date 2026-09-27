import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';

export type ConversationKind = 1 | 2 | 3;

export const CONVERSATION_BOARD: ConversationKind = 1;
export const CONVERSATION_SQUAD: ConversationKind = 2;
export const CONVERSATION_DIRECT: ConversationKind = 3;

export interface ConversationSummary {
  id: string;
  kind: ConversationKind;
  kindLabel: string;
  title: string;
  subtitle: string | null;
  boardId: string | null;
  squadName: string | null;
  counterpartUserId: string | null;
  counterpartInitials: string | null;
  counterpartColor: string | null;
  lastMessagePreview: string | null;
  lastMessageAuthor: string | null;
  lastActivityAt: string;
  unreadCount: number;
  unreadMentionCount: number;
  isMuted: boolean;
  isJoined: boolean;
}

export interface ChatMessage {
  id: string;
  authorUserId: string;
  authorName: string;
  authorInitials: string;
  authorColor: string;
  authorPersonId: string | null;
  body: string;
  sentAt: string;
  editedAt: string | null;
  isDeleted: boolean;
  isMine: boolean;
  mentionsMe: boolean;
  replyToMessageId: string | null;
  replyToAuthorName: string | null;
  replyToExcerpt: string | null;
}

export interface ConversationBoardRef {
  id: string;
  title: string;
  product: string;
  statusLabel: string;
}

export interface ConversationParticipant {
  userId: string;
  personId: string | null;
  displayName: string;
  initials: string;
  color: string;
  headline: string | null;
  /** False when this squad member cannot sign in, so a mention will not reach them. */
  hasAccount: boolean;
  lastReadAt: string | null;
}

export interface ConversationThread {
  id: string;
  kind: ConversationKind;
  kindLabel: string;
  title: string;
  subtitle: string | null;
  boardId: string | null;
  squadName: string | null;
  relatedBoards: ConversationBoardRef[];
  participants: ConversationParticipant[];
  messages: ChatMessage[];
  isMuted: boolean;
  canPost: boolean;
}

export interface MessageTarget {
  kind: 'Board' | 'Squad' | 'Direct';
  label: string;
  sublabel: string | null;
  boardId: string | null;
  squadName: string | null;
  userId: string | null;
  initials: string | null;
  color: string | null;
}

export interface UnreadSummary {
  conversations: number;
  messages: number;
  mentions: number;
}

@Injectable({ providedIn: 'root' })
export class MessagesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * The badge on the navigation rail. Held here rather than in the shell so any screen
   * that changes read state can refresh it — the count is wrong the moment you open a
   * thread, and a stale badge trains people to ignore the badge.
   */
  private readonly unreadSignal = signal<UnreadSummary>({
    conversations: 0,
    messages: 0,
    mentions: 0,
  });
  readonly unread = this.unreadSignal.asReadonly();

  private get url(): string {
    return `${this.baseUrl}/messages`;
  }

  inbox(search?: string): Promise<ConversationSummary[]> {
    const params: Record<string, string> = {};
    if (search?.trim()) params['q'] = search.trim();

    return firstValueFrom(this.http.get<ConversationSummary[]>(`${this.url}/inbox`, { params }));
  }

  thread(conversationId: string, take = 200): Promise<ConversationThread> {
    return firstValueFrom(
      this.http.get<ConversationThread>(`${this.url}/${conversationId}`, {
        params: { take: String(take) },
      }),
    );
  }

  targets(search?: string): Promise<MessageTarget[]> {
    const params: Record<string, string> = {};
    if (search?.trim()) params['q'] = search.trim();

    return firstValueFrom(this.http.get<MessageTarget[]>(`${this.url}/targets`, { params }));
  }

  async refreshUnread(): Promise<UnreadSummary> {
    const summary = await firstValueFrom(
      this.http.get<UnreadSummary>(`${this.url}/unread`),
    );
    this.unreadSignal.set(summary);
    return summary;
  }

  // -- opening ------------------------------------------------------------

  openBoard(boardId: string): Promise<{ conversationId: string }> {
    return firstValueFrom(
      this.http.post<{ conversationId: string }>(`${this.url}/boards/${boardId}`, {}),
    );
  }

  openSquad(squadName: string): Promise<{ conversationId: string }> {
    return firstValueFrom(
      this.http.post<{ conversationId: string }>(`${this.url}/squads`, { squadName }),
    );
  }

  openDirect(userId: string): Promise<{ conversationId: string }> {
    return firstValueFrom(
      this.http.post<{ conversationId: string }>(`${this.url}/direct/${userId}`, {}),
    );
  }

  // -- writing ------------------------------------------------------------

  post(conversationId: string, body: string, replyToMessageId?: string): Promise<ChatMessage> {
    return firstValueFrom(
      this.http.post<ChatMessage>(`${this.url}/${conversationId}/posts`, {
        body,
        replyToMessageId: replyToMessageId ?? null,
      }),
    );
  }

  edit(messageId: string, body: string): Promise<ChatMessage> {
    return firstValueFrom(this.http.put<ChatMessage>(`${this.url}/posts/${messageId}`, { body }));
  }

  remove(messageId: string): Promise<ChatMessage> {
    return firstValueFrom(this.http.delete<ChatMessage>(`${this.url}/posts/${messageId}`));
  }

  // -- housekeeping -------------------------------------------------------

  markRead(conversationId: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.url}/${conversationId}/read`, {}));
  }

  mute(conversationId: string, muted: boolean): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.url}/${conversationId}/mute`, { muted }));
  }

  leave(conversationId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.url}/${conversationId}/membership`));
  }
}
