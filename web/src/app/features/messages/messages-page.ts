import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { LowerCasePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { MessageRealtimeService } from '../../core/services/message-realtime.service';
import {
  CONVERSATION_BOARD,
  CONVERSATION_DIRECT,
  CONVERSATION_SQUAD,
  MessagesService,
  type ChatMessage,
  type ConversationSummary,
  type ConversationThread,
  type MessageTarget,
} from '../../core/services/messages.service';

/**
 * Team messaging.
 *
 * Two panes, because the two questions are asked at different moments: "what needs me"
 * (the inbox, unread first) and "what did we decide about this board" (the thread). A
 * single-pane chat forces you back to a list every time you want to check the other.
 *
 * Channels arrive in the inbox when you open them, not when a board is created. Thirty
 * boards and fourteen squads would otherwise fill the list with rooms nobody has spoken
 * in, and the two conversations that matter to you would be below the fold.
 */
@Component({
  selector: 'app-messages-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, LowerCasePipe],
  templateUrl: './messages-page.html',
  styleUrl: './messages-page.scss',
})
export class MessagesPage {
  private readonly messages = inject(MessagesService);
  private readonly realtime = inject(MessageRealtimeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  protected readonly BOARD = CONVERSATION_BOARD;
  protected readonly SQUAD = CONVERSATION_SQUAD;
  protected readonly DIRECT = CONVERSATION_DIRECT;

  protected readonly inbox = signal<ConversationSummary[]>([]);
  protected readonly thread = signal<ConversationThread | null>(null);
  protected readonly loadingInbox = signal(true);
  protected readonly loadingThread = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');

  protected readonly draft = signal('');
  protected readonly sending = signal(false);
  protected readonly replyTo = signal<ChatMessage | null>(null);
  protected readonly editing = signal<ChatMessage | null>(null);

  /** The "new conversation" picker. */
  protected readonly pickerOpen = signal(false);
  protected readonly pickerSearch = signal('');
  protected readonly targets = signal<MessageTarget[]>([]);

  protected readonly status = this.realtime.status;
  protected readonly unread = this.messages.unread;

  protected readonly myUserId = computed(() => this.auth.user()?.id ?? null);

  protected readonly boardTargets = computed(() =>
    this.targets().filter((t) => t.kind === 'Board'),
  );
  protected readonly squadTargets = computed(() =>
    this.targets().filter((t) => t.kind === 'Squad'),
  );
  protected readonly peopleTargets = computed(() =>
    this.targets().filter((t) => t.kind === 'Direct'),
  );

  /** Squad members with no account: a mention will never reach them. */
  protected readonly unreachable = computed(
    () => this.thread()?.participants.filter((p) => !p.hasAccount) ?? [],
  );

  constructor() {
    void this.loadInbox();
    void this.messages.refreshUnread();

    // Deep links: /messages/:id, and the board editor's Discuss button lands here.
    this.route.paramMap.subscribe((params) => {
      const id = params.get('id');
      if (id) void this.openConversation(id);
    });

    // A pushed message lands in the open thread. Appended rather than refetched so the
    // view does not scroll-jump while somebody is reading back through it.
    effect(() => {
      const incoming = this.realtime.incoming();
      const current = this.thread();
      if (!incoming || !current) return;

      this.thread.set({
        ...current,
        messages: mergeMessage(current.messages, incoming),
      });

      void this.messages.markRead(current.id).then(() => this.messages.refreshUnread());
    });
  }

  // -- inbox --------------------------------------------------------------

  protected async loadInbox(): Promise<void> {
    this.loadingInbox.set(true);

    try {
      this.inbox.set(await this.messages.inbox(this.search()));
      this.error.set(null);
    } catch (err) {
      this.error.set(problemText(err, 'Could not load your conversations.'));
    } finally {
      this.loadingInbox.set(false);
    }
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    void this.loadInbox();
  }

  // -- a thread -----------------------------------------------------------

  protected async openConversation(conversationId: string): Promise<void> {
    this.loadingThread.set(true);
    this.replyTo.set(null);
    this.editing.set(null);

    try {
      const thread = await this.messages.thread(conversationId);
      this.thread.set(thread);
      this.error.set(null);

      await this.messages.markRead(conversationId);
      await this.messages.refreshUnread();
      await this.realtime.join(conversationId);
      await this.loadInbox();
    } catch (err) {
      this.error.set(problemText(err, 'Could not open that conversation.'));
      this.thread.set(null);
    } finally {
      this.loadingThread.set(false);
    }
  }

  protected select(summary: ConversationSummary): void {
    void this.router.navigate(['/messages', summary.id]);
  }

  // -- writing ------------------------------------------------------------

  protected async send(): Promise<void> {
    const body = this.draft().trim();
    const current = this.thread();
    if (!body || !current || this.sending()) return;

    this.sending.set(true);

    try {
      const editingMessage = this.editing();

      if (editingMessage) {
        const updated = await this.messages.edit(editingMessage.id, body);
        this.thread.set({ ...current, messages: mergeMessage(current.messages, updated) });
        this.editing.set(null);
      } else {
        const posted = await this.messages.post(current.id, body, this.replyTo()?.id);
        this.thread.set({ ...current, messages: mergeMessage(current.messages, posted) });
        this.replyTo.set(null);
      }

      this.draft.set('');
      await this.loadInbox();
      await this.messages.refreshUnread();
    } catch (err) {
      this.error.set(problemText(err, 'Your message was not sent.'));
    } finally {
      this.sending.set(false);
    }
  }

  /** Enter sends; Shift+Enter is a newline. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }

  protected startReply(message: ChatMessage): void {
    this.editing.set(null);
    this.replyTo.set(message);
  }

  protected startEdit(message: ChatMessage): void {
    this.replyTo.set(null);
    this.editing.set(message);
    this.draft.set(message.body);
  }

  protected cancelComposerContext(): void {
    this.replyTo.set(null);
    this.editing.set(null);
    this.draft.set('');
  }

  protected async remove(message: ChatMessage): Promise<void> {
    const current = this.thread();
    if (!current) return;

    try {
      const updated = await this.messages.remove(message.id);
      this.thread.set({ ...current, messages: mergeMessage(current.messages, updated) });
      await this.loadInbox();
    } catch (err) {
      this.error.set(problemText(err, 'That message was not withdrawn.'));
    }
  }

  /** Inserts "@Name " at the end of the draft, for the mention chips. */
  protected mention(displayName: string): void {
    const draft = this.draft();
    const separator = draft.length === 0 || draft.endsWith(' ') ? '' : ' ';
    this.draft.set(`${draft}${separator}@${displayName} `);
  }

  // -- housekeeping -------------------------------------------------------

  protected async toggleMute(): Promise<void> {
    const current = this.thread();
    if (!current) return;

    try {
      await this.messages.mute(current.id, !current.isMuted);
      this.thread.set({ ...current, isMuted: !current.isMuted });
      await this.loadInbox();
      await this.messages.refreshUnread();
    } catch (err) {
      this.error.set(problemText(err, 'Could not change that setting.'));
    }
  }

  protected async leave(): Promise<void> {
    const current = this.thread();
    if (!current || current.kind === CONVERSATION_DIRECT) return;

    try {
      await this.messages.leave(current.id);
      this.thread.set(null);
      await this.realtime.leave();
      void this.router.navigate(['/messages']);
      await this.loadInbox();
      await this.messages.refreshUnread();
    } catch (err) {
      this.error.set(problemText(err, 'Could not leave that conversation.'));
    }
  }

  // -- starting something new ---------------------------------------------

  protected async openPicker(): Promise<void> {
    this.pickerOpen.set(true);
    await this.loadTargets();
  }

  protected closePicker(): void {
    this.pickerOpen.set(false);
    this.pickerSearch.set('');
  }

  protected async loadTargets(): Promise<void> {
    try {
      this.targets.set(await this.messages.targets(this.pickerSearch()));
    } catch (err) {
      this.error.set(problemText(err, 'Could not load the list.'));
    }
  }

  protected onPickerSearch(value: string): void {
    this.pickerSearch.set(value);
    void this.loadTargets();
  }

  protected async start(target: MessageTarget): Promise<void> {
    try {
      const ref =
        target.kind === 'Board'
          ? await this.messages.openBoard(target.boardId!)
          : target.kind === 'Squad'
            ? await this.messages.openSquad(target.squadName!)
            : await this.messages.openDirect(target.userId!);

      this.closePicker();
      void this.router.navigate(['/messages', ref.conversationId]);
    } catch (err) {
      this.error.set(problemText(err, 'Could not start that conversation.'));
    }
  }

  // -- presentation -------------------------------------------------------

  /** "09:41" today, "Mon 09:41" this week, otherwise a date. */
  protected timeLabel(iso: string): string {
    const at = new Date(iso);
    const now = new Date();
    const sameDay = at.toDateString() === now.toDateString();

    if (sameDay) {
      return at.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
    }

    const days = (now.getTime() - at.getTime()) / 86_400_000;

    if (days < 7) {
      return at.toLocaleString(undefined, {
        weekday: 'short',
        hour: '2-digit',
        minute: '2-digit',
      });
    }

    return at.toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
  }

  /** Whether a date separator belongs above this message. */
  protected startsNewDay(messages: ChatMessage[], index: number): boolean {
    if (index === 0) return true;
    return (
      new Date(messages[index].sentAt).toDateString() !==
      new Date(messages[index - 1].sentAt).toDateString()
    );
  }

  protected dayLabel(iso: string): string {
    const at = new Date(iso);
    const today = new Date();
    if (at.toDateString() === today.toDateString()) return 'Today';

    const yesterday = new Date(today);
    yesterday.setDate(today.getDate() - 1);
    if (at.toDateString() === yesterday.toDateString()) return 'Yesterday';

    return at.toLocaleDateString(undefined, {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
    });
  }
}

/**
 * Replaces a message in place, or appends it.
 *
 * One function for posts, edits, withdrawals and the live push, because the socket
 * re-delivers a message the sender already has locally — appending blindly shows it twice.
 */
function mergeMessage(messages: ChatMessage[], incoming: ChatMessage): ChatMessage[] {
  const index = messages.findIndex((m) => m.id === incoming.id);

  if (index >= 0) {
    const copy = [...messages];
    copy[index] = incoming;
    return copy;
  }

  return [...messages, incoming];
}

function problemText(err: unknown, fallback: string): string {
  const problem = (err as { error?: { detail?: string; title?: string } })?.error;
  return problem?.detail ?? problem?.title ?? fallback;
}
