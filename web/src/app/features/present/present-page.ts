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
import type { BoardDetail } from '../../core/models/board.models';

/**
 * Distraction-free full-screen presenting (spec FR-6).
 *
 * Arrow keys and Space step through the portfolio, Esc returns to where you came from.
 * Entering at /present/:id starts on that board but still loads the rest, so a reviewer
 * can keep walking the portfolio without leaving the mode.
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

  protected readonly current = computed<BoardDetail | null>(
    () => this.boards()[this.index()] ?? null,
  );

  protected readonly hasPrevious = computed(() => this.index() > 0);
  protected readonly hasNext = computed(() => this.index() < this.boards().length - 1);

  constructor() {
    const startId = this.route.snapshot.paramMap.get('id');
    this.load(startId);
  }

  private load(startId: string | null): void {
    this.boardsService.list({ pageSize: 200 }).subscribe({
      next: (page) => {
        if (page.items.length === 0) {
          this.error.set('There are no boards to present.');
          this.loading.set(false);
          return;
        }

        Promise.all(
          page.items.map(
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

          const startIndex = startId ? loaded.findIndex((b) => b.id === startId) : 0;
          this.index.set(startIndex >= 0 ? startIndex : 0);
          this.loading.set(false);
        });
      },
      error: () => {
        this.error.set('The portfolio could not be loaded.');
        this.loading.set(false);
      },
    });
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
        event.preventDefault();
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
