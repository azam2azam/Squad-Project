import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { AuthService } from './auth.service';
import type { ChatMessage } from './messages.service';

export type RealtimeStatus = 'disconnected' | 'connecting' | 'live';

/**
 * Live messages over SignalR.
 *
 * Fail-soft like the board hub: a chat that cannot reach the socket still sends and
 * receives on reload, and says it is not live rather than pretending. Losing the socket
 * must never cost somebody the message they are typing.
 *
 * Unlike the board hub this pushes the message itself rather than a "something changed"
 * nudge. A thread refetching in full on every keystroke elsewhere in the squad would be
 * both slow and visibly jumpy.
 */
@Injectable({ providedIn: 'root' })
export class MessageRealtimeService {
  private readonly auth = inject(AuthService);

  private connection?: HubConnection;
  private joinedId: string | null = null;

  private readonly statusSignal = signal<RealtimeStatus>('disconnected');
  readonly status = this.statusSignal.asReadonly();

  /** The last message pushed by the server, for the open thread. */
  private readonly incomingSignal = signal<ChatMessage | null>(null);
  readonly incoming = this.incomingSignal.asReadonly();

  constructor() {
    inject(DestroyRef).onDestroy(() => void this.disconnect());
  }

  async join(conversationId: string): Promise<void> {
    try {
      await this.ensureConnected();

      if (this.joinedId === conversationId) return;

      if (this.joinedId) {
        await this.connection!.invoke('LeaveConversation', this.joinedId);
      }

      await this.connection!.invoke('JoinConversation', conversationId);
      this.joinedId = conversationId;
    } catch {
      // The server refuses a join it has not authorised, which lands here alongside a
      // plain network failure. Either way the thread works without live updates.
      this.statusSignal.set('disconnected');
    }
  }

  async leave(): Promise<void> {
    if (!this.connection || !this.joinedId) return;

    try {
      await this.connection.invoke('LeaveConversation', this.joinedId);
    } catch {
      // Best-effort — the server drops the group on disconnect anyway.
    }
    this.joinedId = null;
  }

  private async ensureConnected(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return;

    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        // The hub is [Authorize]d and opens its own socket, so the HTTP interceptor
        // cannot reach it — the token goes in the query string, which the server reads.
        .withUrl('/hubs/messages', { accessTokenFactory: () => this.auth.accessToken ?? '' })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      this.connection.on('MessagePosted', (m: ChatMessage) => this.incomingSignal.set(m));
      this.connection.on('MessageChanged', (m: ChatMessage) => this.incomingSignal.set(m));

      this.connection.onreconnecting(() => this.statusSignal.set('connecting'));
      this.connection.onreconnected(async () => {
        this.statusSignal.set('live');
        // Group membership does not survive a reconnect — rejoin explicitly.
        if (this.joinedId) {
          const id = this.joinedId;
          this.joinedId = null;
          await this.join(id);
        }
      });
      this.connection.onclose(() => this.statusSignal.set('disconnected'));
    }

    this.statusSignal.set('connecting');
    await this.connection.start();
    this.statusSignal.set('live');
  }

  private async disconnect(): Promise<void> {
    if (!this.connection) return;
    try {
      await this.connection.stop();
    } catch {
      // Nothing useful to do while tearing down.
    }
  }
}
