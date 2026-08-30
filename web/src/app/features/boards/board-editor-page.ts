import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { BoardsService } from '../../core/services/boards.service';
import { MetadataService } from '../../core/services/metadata.service';
import { SlideCanvas } from '../../shared/slide/slide-canvas';
import { SquadEditor } from './squad-editor';
import { BoardRealtimeService } from '../../core/services/board-realtime.service';
import { SlideExportService } from '../../core/services/slide-export.service';
import type { BoardDetail, BoardStatus } from '../../core/models/board.models';

type MobileTab = 'build' | 'slide';

/**
 * The prototype layout: a builder form on the left driving a live, sticky slide on the
 * right. Edits update the slide immediately from local signal state; the server is only
 * consulted on save, so typing never waits on a round trip.
 */
@Component({
  selector: 'app-board-editor-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, SlideCanvas, SquadEditor],
  templateUrl: './board-editor-page.html',
  styleUrl: './board-editor-page.scss',
})
export class BoardEditorPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly boards = inject(BoardsService);
  private readonly metadata = inject(MetadataService);
  private readonly realtime = inject(BoardRealtimeService);
  private readonly exporter = inject(SlideExportService);

  protected readonly statuses = this.metadata.statuses;
  protected readonly realtimeStatus = this.realtime.status;
  protected readonly serverExportEnabled = this.metadata.serverExportEnabled;

  /** The rendered slide element, captured for client-side PNG export. */
  private readonly slideHost = viewChild<ElementRef<HTMLElement>>('slideHost');

  protected readonly exporting = signal(false);

  private readonly boardId = toSignal(this.route.paramMap.pipe(map((params) => params.get('id'))), {
    initialValue: null,
  });

  /** The authoritative board as last returned by the server. */
  private readonly serverBoard = signal<BoardDetail | null>(null);

  /** Local edits, applied over the server board to produce the live preview. */
  protected readonly draft = signal<DraftState | null>(null);

  /** Distinguishes "failed to load" from "loaded, then a save failed". */
  protected readonly serverBoardLoaded = computed(() => this.serverBoard() !== null);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly savedAt = signal<Date | null>(null);
  protected readonly mobileTab = signal<MobileTab>('build');

  /**
   * What the slide renders: the server board with the in-progress edits laid over it,
   * including a recomputed status label and colour so the badge tracks the dropdown
   * without waiting for a save.
   */
  protected readonly preview = computed<BoardDetail | null>(() => {
    const board = this.serverBoard();
    const draft = this.draft();
    if (!board || !draft) return null;

    const status = this.metadata.statusOption(draft.status);

    return {
      ...board,
      title: draft.title.trim() || 'Untitled board',
      product: draft.product.trim() || 'Product',
      squadName: draft.squadName.trim() || 'Unnamed squad',
      sprint: draft.sprint.trim() || null,
      status: draft.status,
      statusLabel: status?.label ?? board.statusLabel,
      statusColor: status?.color ?? board.statusColor,
      progressPercent: draft.progressPercent,
      blockerNote: draft.blockerNote.trim() || null,
    };
  });

  protected readonly isDirty = computed(() => {
    const board = this.serverBoard();
    const draft = this.draft();
    if (!board || !draft) return false;

    return (
      draft.title !== board.title ||
      draft.product !== board.product ||
      draft.squadName !== board.squadName ||
      draft.sprint !== (board.sprint ?? '') ||
      draft.status !== board.status ||
      draft.progressPercent !== board.progressPercent ||
      draft.blockerNote !== (board.blockerNote ?? '')
    );
  });

  /** Advisory warnings recomputed locally so they react to the status dropdown. */
  protected readonly warnings = computed<string[]>(() => {
    const board = this.serverBoard();
    const draft = this.draft();
    if (!board || !draft) return [];

    const warnings = board.warnings.filter((w) => !w.startsWith('Status is Blocked'));

    // 2 is Blocked. Recomputed here because the blocker note is edited locally.
    if (draft.status === 2 && !draft.blockerNote.trim()) {
      warnings.push('Status is Blocked but no blocker note has been recorded.');
    }

    return warnings;
  });

  constructor() {
    const id = this.boardId();
    if (id) {
      this.load(id);
      void this.realtime.join(id);
    }

    // Another viewer changed this board. Refetch the server state, but never clobber
    // edits in progress — an unsaved draft belongs to this user, not the broadcast.
    let seen = this.realtime.revision();
    effect(() => {
      const revision = this.realtime.revision();
      if (revision === seen) return;
      seen = revision;

      const board = this.serverBoard();
      if (!board) return;

      // Read before the swap: isDirty() compares the draft against serverBoard, so
      // replacing it first would make a clean editor look dirty and the incoming
      // values would be thrown away.
      const hadLocalEdits = this.isDirty();

      this.boards.get(board.id).subscribe({
        next: (fresh) => {
          this.serverBoard.set(fresh);
          if (!hadLocalEdits) {
            this.draft.set(toDraft(fresh));
          }
        },
        error: () => {
          // A failed refresh is not worth interrupting the user over; the next
          // save will surface any real problem.
        },
      });
    });
  }

  private load(id: string): void {
    this.loading.set(true);
    this.boards.get(id).subscribe({
      next: (board) => {
        this.serverBoard.set(board);
        this.draft.set(toDraft(board));
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(
          err?.status === 404 ? 'That board does not exist.' : 'Could not load the board.',
        );
        this.loading.set(false);
      },
    });
  }

  protected update<K extends keyof DraftState>(key: K, value: DraftState[K]): void {
    this.draft.update((draft) => (draft ? { ...draft, [key]: value } : draft));
  }

  protected onProgressInput(value: string): void {
    const parsed = Number(value);
    this.update('progressPercent', Number.isNaN(parsed) ? 0 : Math.max(0, Math.min(100, parsed)));
  }

  protected onStatusChange(value: string): void {
    this.update('status', Number(value) as BoardStatus);
  }

  protected save(): void {
    const board = this.serverBoard();
    const draft = this.draft();
    if (!board || !draft || this.saving()) return;

    this.saving.set(true);
    this.error.set(null);

    this.boards
      .update(board.id, {
        title: draft.title.trim(),
        product: draft.product.trim(),
        squadName: draft.squadName.trim(),
        sprint: draft.sprint.trim() || null,
        status: draft.status,
        progressPercent: draft.progressPercent,
        blockerNote: draft.blockerNote.trim() || null,
      })
      .subscribe({
        next: (saved) => {
          this.serverBoard.set(saved);
          this.draft.set(toDraft(saved));
          this.saving.set(false);
          this.savedAt.set(new Date());
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(readProblemDetail(err) ?? 'Could not save the board.');
        },
      });
  }

  /**
   * Refetches after a membership change. Only the server-owned parts are replaced —
   * the draft is left alone, so adding somebody mid-edit does not silently discard
   * a title the user was halfway through typing.
   */
  protected reloadMembers(): void {
    const board = this.serverBoard();
    if (!board) return;

    this.boards.get(board.id).subscribe({
      next: (fresh) => this.serverBoard.set(fresh),
      error: () => this.error.set('Could not refresh the squad.'),
    });
  }

  /** Client-side PNG of exactly what is on screen (spec FR-7). */
  protected async downloadPng(): Promise<void> {
    const host = this.slideHost();
    const board = this.preview();
    if (!host || !board || this.exporting()) return;

    this.exporting.set(true);
    this.error.set(null);

    try {
      await this.exporter.downloadPng(host.nativeElement, board.title);
    } catch {
      this.error.set('Could not capture the slide as a PNG.');
    } finally {
      this.exporting.set(false);
    }
  }

  /** Server-rendered PDF; only offered when the host actually has a renderer. */
  protected downloadPdf(): void {
    const board = this.serverBoard();
    if (!board) return;

    window.open(`/api/v1/boards/${board.id}/export/pdf`, '_blank');
  }

  protected present(): void {
    const board = this.serverBoard();
    if (board) {
      void this.router.navigate(['/present', board.id]);
    }
  }

  protected revert(): void {
    const board = this.serverBoard();
    if (board) {
      this.draft.set(toDraft(board));
      this.error.set(null);
    }
  }

  protected duplicate(): void {
    const board = this.serverBoard();
    if (!board) return;

    this.boards.duplicate(board.id).subscribe({
      next: (copy) => void this.router.navigate(['/boards', copy.id]),
      error: () => this.error.set('Could not duplicate the board.'),
    });
  }
}

/** Editable fields, held as strings where the input is a text box. */
interface DraftState {
  title: string;
  product: string;
  squadName: string;
  sprint: string;
  status: BoardStatus;
  progressPercent: number;
  blockerNote: string;
}

function toDraft(board: BoardDetail): DraftState {
  return {
    title: board.title,
    product: board.product,
    squadName: board.squadName,
    sprint: board.sprint ?? '',
    status: board.status,
    progressPercent: board.progressPercent,
    blockerNote: board.blockerNote ?? '',
  };
}

/** Surfaces the server's RFC 7807 message instead of a generic failure string. */
function readProblemDetail(err: unknown): string | null {
  const problem = (err as { error?: { detail?: string; title?: string } })?.error;
  return problem?.detail ?? problem?.title ?? null;
}
