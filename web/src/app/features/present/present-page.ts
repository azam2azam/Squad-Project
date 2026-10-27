import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { BoardsService } from '../../core/services/boards.service';
import { SlideCanvas } from '../../shared/slide/slide-canvas';
import type { BoardDetail, BoardSummary } from '../../core/models/board.models';

/** One programme to present, with how many of its boards are in the deck. */
interface DeckChoice {
  id: string | null;
  name: string;
  color: string | null;
  count: number;
}

const THEME_KEY = 'ssb.present.theme';

/**
 * Distraction-free full-screen presenting (spec FR-6).
 *
 * Arrow keys and Space step through the deck, Esc returns to where you came from.
 *
 * The deck is a programme, not the whole portfolio: a review meeting walks one programme
 * at a time, and at 30 boards "everything" was never the right deck. A board can also be
 * kept out of Present mode entirely without being deleted.
 */
@Component({
  selector: 'app-present-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SlideCanvas],
  templateUrl: './present-page.html',
  styleUrl: './present-page.scss',
})
export class PresentPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly boardsService = inject(BoardsService);

  protected readonly boards = signal<BoardDetail[]>([]);
  protected readonly index = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  /** Null until a programme is picked, which is what the chooser is for. */
  protected readonly choices = signal<DeckChoice[] | null>(null);

  /** The slide's own palette. Dark is the deliverable; light is for bright rooms. */
  protected readonly theme = signal<'dark' | 'light'>(readStoredTheme());

  private summaries: BoardSummary[] = [];
  private startId: string | null = null;

  protected readonly current = computed<BoardDetail | null>(
    () => this.boards()[this.index()] ?? null,
  );

  protected readonly hasPrevious = computed(() => this.index() > 0);
  protected readonly hasNext = computed(() => this.index() < this.boards().length - 1);

  constructor() {
    this.startId = this.route.snapshot.paramMap.get('id');
    this.load();
  }

  private load(): void {
    this.boardsService.list({ pageSize: 200 }).subscribe({
      next: (page) => {
        // Boards explicitly kept out of the deck never reach the chooser either, so its
        // counts match what the meeting will actually see.
        this.summaries = page.items.filter((b) => b.includeInPresentation);

        if (this.summaries.length === 0) {
          this.error.set('No boards are set to appear in Present mode.');
          this.loading.set(false);
          return;
        }

        // Entering on a specific board skips the chooser: the presenter already chose.
        const requested = this.route.snapshot.queryParamMap.get('category');

        if (this.startId) {
          const start = this.summaries.find((b) => b.id === this.startId);
          this.open(start ? start.categoryId : null);
          return;
        }

        if (requested !== null) {
          this.open(requested === '' ? null : requested);
          return;
        }

        const choices = this.buildChoices();

        // One programme is not a choice; go straight in.
        if (choices.length <= 1) {
          this.open(choices[0]?.id ?? null);
          return;
        }

        this.choices.set(choices);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('The portfolio could not be loaded.');
        this.loading.set(false);
      },
    });
  }

  private buildChoices(): DeckChoice[] {
    const byCategory = new Map<string, DeckChoice>();

    for (const board of this.summaries) {
      const key = board.categoryId ?? '';
      const existing = byCategory.get(key);

      if (existing) {
        existing.count++;
        continue;
      }

      byCategory.set(key, {
        id: board.categoryId,
        name: board.categoryName ?? 'Uncategorised',
        color: board.categoryColor,
        count: 1,
      });
    }

    return [...byCategory.values()].sort((a, b) => a.name.localeCompare(b.name));
  }

  /** Loads the full boards for one programme, or all of them when given null twice. */
  protected open(categoryId: string | null, all = false): void {
    const deck = all
      ? this.summaries
      : this.summaries.filter((b) => (b.categoryId ?? null) === categoryId);

    if (deck.length === 0) {
      this.error.set('That programme has no boards to present.');
      this.loading.set(false);
      return;
    }

    this.choices.set(null);
    this.loading.set(true);

    Promise.all(
      deck.map(
        (summary) =>
          new Promise<BoardDetail | null>((resolve) => {
            this.boardsService.get(summary.id).subscribe({
              next: resolve,
              error: () => resolve(null),
            });
          }),
      ),
    ).then((details) => {
      const loaded = details.filter((b): b is BoardDetail => b !== null);
      this.boards.set(loaded);

      const startIndex = this.startId ? loaded.findIndex((b) => b.id === this.startId) : 0;
      this.index.set(startIndex >= 0 ? startIndex : 0);
      this.loading.set(false);
    });
  }

  protected presentEverything(): void {
    this.open(null, true);
  }

  protected toggleTheme(): void {
    const next = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(next);

    try {
      localStorage.setItem(THEME_KEY, next);
    } catch {
      // A private window refusing storage is not worth interrupting a presentation.
    }
  }

  protected next(): void {
    if (this.hasNext()) this.index.update((i) => i + 1);
  }

  protected previous(): void {
    if (this.hasPrevious()) this.index.update((i) => i - 1);
  }

  protected exit(): void {
    const current = this.current();
    void this.router.navigate(current ? ['/boards', current.id] : ['/portfolio']);
  }

  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    // The chooser is a menu, not a deck: arrow keys would step through nothing.
    if (this.choices() !== null) {
      if (event.key === 'Escape') this.exit();
      return;
    }

    switch (event.key) {
      case 'ArrowRight':
      case 'PageDown':
      case ' ':
        event.preventDefault();
        this.next();
        break;
      case 'ArrowLeft':
      case 'PageUp':
        event.preventDefault();
        this.previous();
        break;
      case 'Escape':
        this.exit();
        break;
      case 'Home':
        event.preventDefault();
        this.index.set(0);
        break;
      case 'End':
        event.preventDefault();
        this.index.set(this.boards().length - 1);
        break;
    }
  }
}

function readStoredTheme(): 'dark' | 'light' {
  try {
    return localStorage.getItem(THEME_KEY) === 'light' ? 'light' : 'dark';
  } catch {
    return 'dark';
  }
}
