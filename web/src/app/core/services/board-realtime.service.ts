import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';

export type RealtimeStatus = 'disconnected' | 'connecting' | 'live';

/**
 * Live board updates over SignalR.
 *
 * Deliberately fail-soft: if the hub cannot be reached the app keeps working as a normal
 * request/response client and simply reports that it is not live. Losing the socket must
 * never cost somebody the edit they are in the middle of.
 */
@Injectable({ providedIn: 'root' })
export class BoardRealtimeService {
  private connection?: HubConnection;
  private joinedBoardId: string | null = null;

  private readonly statusSignal = signal<RealtimeStatus>('disconnected');
  readonly status = this.statusSignal.asReadonly();

  /** Bumped whenever the server says this board changed, so callers can refetch. */
  private readonly revisionSignal = signal(0);
  readonly revision = this.revisionSignal.asReadonly();

  constructor() {
    inject(DestroyRef).onDestroy(() => void this.disconnect());
  }

  /**
   * Joins a board's group, connecting first if needed. Safe to call repeatedly with the
   * same id, and switching boards leaves the previous group.
   */
  async join(boardId: string): Promise<void> {
    try {
      await this.ensureConnected();

      if (this.joinedBoardId === boardId) return;

      if (this.joinedBoardId) {
        await this.connection!.invoke('LeaveBoard', this.joinedBoardId);
      }

      await this.connection!.invoke('JoinBoard', boardId);
      this.joinedBoardId = boardId;
    } catch {
      // Reported through status(); the caller carries on without live updates.
      this.statusSignal.set('disconnected');
    }
  }

  async leave(): Promise<void> {
    if (!this.connection || !this.joinedBoardId) return;

    try {
      await this.connection.invoke('LeaveBoard', this.joinedBoardId);
    } catch {
      // Leaving is best-effort — the server drops the group on disconnect anyway.
    }
    this.joinedBoardId = null;
  }

  private async ensureConnected(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return;

    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        .withUrl('/hubs/boards')
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      // Both events mean the same thing to a viewer: this board is stale, refetch it.
      this.connection.on('BoardUpdated', () => this.revisionSignal.update((r) => r + 1));
      this.connection.on('MemberChanged', () => this.revisionSignal.update((r) => r + 1));

      this.connection.onreconnecting(() => this.statusSignal.set('connecting'));
      this.connection.onreconnected(async () => {
        this.statusSignal.set('live');
        // Group membership does not survive a reconnect — rejoin explicitly.
        if (this.joinedBoardId) {
          const boardId = this.joinedBoardId;
          this.joinedBoardId = null;
          await this.join(boardId);
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
