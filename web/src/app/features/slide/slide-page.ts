import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { BoardsService } from '../../core/services/boards.service';
import { SlideCanvas } from '../../shared/slide/slide-canvas';
import type { BoardDetail } from '../../core/models/board.models';

/**
 * The slide, alone on the page, outside the app shell.
 *
 * This is the route the headless export renderer loads, which is why it carries no
 * navigation, no chrome and no controls: what Chromium screenshots is exactly the
 * component the user sees in the editor.
 *
 * `/slide/all` renders every board stacked with a page break between them, so the
 * portfolio PDF is one paginated render rather than N documents to merge.
 */
@Component({
  selector: 'app-slide-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SlideCanvas],
  template: `
    @if (error()) {
      <p class="slide-page__error">{{ error() }}</p>
    } @else {
      <div class="slide-page" [class.slide-page--multi]="boards().length > 1">
        @for (board of boards(); track board.id) {
          <div class="slide-page__sheet">
            <app-slide-canvas [board]="board" />
          </div>
        }
      </div>

      @if (ready()) {
        <!-- The renderer waits for this before capturing, so it never screenshots
             a half-loaded slide. -->
        <div data-export-ready="true" hidden></div>
      }
    }
  `,
  styles: `
    :host {
      display: block;
      background: var(--ink);
    }

    .slide-page {
      display: flex;
      flex-direction: column;
    }

    .slide-page__sheet {
      width: 1280px;
      max-width: 100vw;
    }

    /* One slide per page in the portfolio PDF. */
    .slide-page--multi .slide-page__sheet {
      break-after: page;
      page-break-after: always;
    }

    .slide-page--multi .slide-page__sheet:last-child {
      break-after: auto;
      page-break-after: auto;
    }

    .slide-page__error {
      color: var(--tx);
      font-family: var(--font-ui);
      padding: 40px;
    }
  `,
})
export class SlidePage {
  private readonly route = inject(ActivatedRoute);
  private readonly boardsService = inject(BoardsService);

  protected readonly boards = signal<BoardDetail[]>([]);
  protected readonly ready = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly id = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), {
    initialValue: null,
  });

  constructor() {
    const id = this.id();

    if (id === 'all') {
      this.loadAll();
    } else if (id) {
      this.loadOne(id);
    } else {
      this.error.set('No board specified.');
    }
  }

  private loadOne(id: string): void {
    this.boardsService.get(id).subscribe({
      next: (board) => {
        this.boards.set([board]);
        this.ready.set(true);
      },
      error: () => this.error.set('That board could not be loaded.'),
    });
  }

  /**
   * The list endpoint returns summaries, but the slide needs full detail, so each
   * board is fetched. Sequential-safe via forkJoin-style Promise.all on subscribe.
   */
  private loadAll(): void {
    this.boardsService.list({ pageSize: 200 }).subscribe({
      next: (page) => {
        if (page.items.length === 0) {
          this.error.set('There are no boards to export.');
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
          this.boards.set(details.filter((b): b is BoardDetail => b !== null));
          this.ready.set(true);
        });
      },
      error: () => this.error.set('The portfolio could not be loaded.'),
    });
  }
}
