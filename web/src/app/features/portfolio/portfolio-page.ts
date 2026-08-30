import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { BoardsService } from '../../core/services/boards.service';
import { MetadataService } from '../../core/services/metadata.service';
import type { BoardStatus, BoardSummary } from '../../core/models/board.models';

/**
 * The exec-level read: every board as a compact card showing title, squad, status and
 * progress, mirroring the slide's colour system so the portfolio and the slides look
 * like one product.
 */
@Component({
  selector: 'app-portfolio-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './portfolio-page.html',
  styleUrl: './portfolio-page.scss',
})
export class PortfolioPage {
  private readonly boards = inject(BoardsService);
  private readonly metadata = inject(MetadataService);
  private readonly router = inject(Router);

  protected readonly statuses = this.metadata.statuses;

  protected readonly items = signal<BoardSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly search = signal('');
  protected readonly statusFilter = signal<BoardStatus | null>(null);

  protected readonly totalCount = signal(0);

  protected readonly isEmpty = computed(() => !this.loading() && this.items().length === 0);

  /** True when the list is empty only because of the filters, not because there are none. */
  protected readonly isFiltered = computed(
    () => this.search().trim().length > 0 || this.statusFilter() !== null,
  );

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);

    this.boards
      .list({
        q: this.search().trim() || undefined,
        status: this.statusFilter() ?? undefined,
      })
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load boards.');
          this.loading.set(false);
        },
      });
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.reload();
  }

  protected onStatusFilter(value: string): void {
    this.statusFilter.set(value === '' ? null : (Number(value) as BoardStatus));
    this.reload();
  }

  /** Creates a blank board and drops the user straight into the editor. */
  protected createBoard(): void {
    if (this.creating()) return;
    this.creating.set(true);

    this.boards
      .create({
        title: 'New board',
        product: 'VIDA HIS',
        squadName: 'New squad',
        sprint: null,
        status: 0,
        progressPercent: 0,
      })
      .subscribe({
        next: (board) => {
          this.creating.set(false);
          void this.router.navigate(['/boards', board.id]);
        },
        error: () => {
          this.creating.set(false);
          this.error.set('Could not create a board.');
        },
      });
  }

  protected deleteBoard(board: BoardSummary, event: Event): void {
    event.stopPropagation();
    event.preventDefault();

    this.boards.delete(board.id).subscribe({
      next: () => this.reload(),
      error: () => this.error.set(`Could not delete "${board.title}".`),
    });
  }
}
