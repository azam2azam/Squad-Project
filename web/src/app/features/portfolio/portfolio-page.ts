import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BoardsService } from '../../core/services/boards.service';
import { MetadataService } from '../../core/services/metadata.service';
import { AuthService } from '../../core/services/auth.service';
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
  private readonly auth = inject(AuthService);

  protected readonly statuses = this.metadata.statuses;

  protected readonly items = signal<BoardSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly search = signal('');
  protected readonly statusFilter = signal<BoardStatus | null>(null);

  protected readonly totalCount = signal(0);
  protected readonly importing = signal(false);
  protected readonly importSummary = signal<string | null>(null);

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

  protected present(): void {
    void this.router.navigate(['/present']);
  }

  /** Downloads every board and the roster as JSON (spec FR-9). */
  protected exportAll(): void {
    void this.download('/api/v1/export', 'squad-status-board.json');
  }

  /** The same data as an editable Excel workbook. */
  protected exportExcel(): void {
    void this.download('/api/v1/export/excel', 'squad-status-board.xlsx');
  }

  protected exportPortfolioPdf(): void {
    void this.download('/api/v1/portfolio/export/pdf', 'squad-portfolio.pdf');
  }

  /**
   * Downloads via fetch rather than window.open: these endpoints require the bearer
   * token, and a new tab would arrive unauthenticated and bounce to the login page.
   */
  private async download(path: string, fallbackName: string): Promise<void> {
    this.error.set(null);

    try {
      const response = await fetch(path, {
        headers: { Authorization: `Bearer ${this.auth.accessToken ?? ''}` },
      });

      if (!response.ok) {
        throw new Error(String(response.status));
      }

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);

      const link = document.createElement('a');
      link.href = url;
      link.download = filenameFrom(response.headers.get('content-disposition')) ?? fallbackName;
      link.click();

      setTimeout(() => URL.revokeObjectURL(url), 0);
    } catch {
      this.error.set('Could not download that export.');
    }
  }

  /** Imports an edited Excel workbook back in. */
  protected async importExcel(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.importing.set(true);
    this.error.set(null);
    this.importSummary.set(null);

    try {
      const body = new FormData();
      body.append('file', file);

      const response = await fetch('/api/v1/import/excel', {
        method: 'POST',
        headers: { Authorization: `Bearer ${this.auth.accessToken ?? ''}` },
        body,
      });

      const payload = await response.json();

      if (!response.ok) {
        // The server names the offending sheet, row and column — surface that rather
        // than a generic failure, because it is the only way to fix the file.
        throw new Error(payload?.detail ?? payload?.title ?? 'Import failed');
      }

      this.importSummary.set(summarise(payload));
      this.reload();
    } catch (err) {
      this.error.set(err instanceof Error ? err.message : 'That workbook could not be imported.');
    } finally {
      this.importing.set(false);
      input.value = '';
    }
  }

  /** Reads a previously exported file back in. Upserts, so re-importing is safe. */
  protected async importFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.importing.set(true);
    this.error.set(null);
    this.importSummary.set(null);

    try {
      const parsed = JSON.parse(await file.text());
      const result = await firstValueFrom(this.boards.import(parsed));

      this.importSummary.set(
        `Imported ${result.boardsCreated} new and ${result.boardsUpdated} updated ${
          result.boardsCreated + result.boardsUpdated === 1 ? 'board' : 'boards'
        }, ${result.peopleCreated + result.peopleUpdated} people.` +
          (result.warnings.length > 0 ? ` ${result.warnings.length} warning(s).` : ''),
      );
      this.reload();
    } catch (err) {
      this.error.set(readProblem(err) ?? 'That file could not be imported.');
    } finally {
      this.importing.set(false);
      // Cleared so re-picking the same file fires change again.
      input.value = '';
    }
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

function readProblem(err: unknown): string | null {
  const problem = (err as { error?: { detail?: string; title?: string } })?.error;
  return problem?.detail ?? problem?.title ?? null;
}

/** Reads the server-supplied filename so the download keeps its dated name. */
function filenameFrom(header: string | null): string | null {
  if (!header) return null;
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(header);
  return match ? decodeURIComponent(match[1]) : null;
}

/** One readable line describing what an import actually changed. */
function summarise(result: {
  boardsCreated: number;
  boardsUpdated: number;
  peopleCreated: number;
  peopleUpdated: number;
  warnings?: string[];
}): string {
  const boards = result.boardsCreated + result.boardsUpdated;
  const people = result.peopleCreated + result.peopleUpdated;
  const warnings = result.warnings?.length ?? 0;

  return (
    `Imported ${result.boardsCreated} new and ${result.boardsUpdated} updated ` +
    `${boards === 1 ? 'board' : 'boards'}, ${people} people.` +
    (warnings > 0 ? ` ${warnings} warning(s).` : '')
  );
}
