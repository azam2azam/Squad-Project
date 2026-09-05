import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/api.config';
import { BoardsService } from '../../core/services/boards.service';
import { MetadataService } from '../../core/services/metadata.service';
import { AuthService } from '../../core/services/auth.service';
import type { BoardStatus, BoardSummary } from '../../core/models/board.models';
import type { PortfolioSummary } from '../../core/models/portfolio.models';

/** One figure in the headline strip. */
interface Kpi {
  label: string;
  value: string;
  tone?: 'warn' | 'bad';
}

/**
 * The delivery command centre: the headline figures, a filter bar, and every board as a
 * card carrying its squad, progress, faces, target release and anything wrong with it.
 *
 * Colours come from the same tokens as the slide, so the portfolio and the deck read as
 * one product rather than two.
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
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  protected readonly statuses = this.metadata.statuses;

  protected readonly items = signal<BoardSummary[]>([]);
  protected readonly summary = signal<PortfolioSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly search = signal('');
  protected readonly statusFilter = signal<BoardStatus | null>(null);
  protected readonly productFilter = signal<string | null>(null);
  protected readonly sprintFilter = signal<string | null>(null);
  protected readonly squadFilter = signal<string | null>(null);
  protected readonly showMoreFilters = signal(false);

  protected readonly totalCount = signal(0);
  protected readonly importing = signal(false);
  protected readonly importSummary = signal<string | null>(null);

  /**
   * The filter dropdowns are built from every board, not the filtered ones — otherwise
   * choosing a product would empty the product list you just chose from.
   */
  private readonly allBoards = signal<BoardSummary[]>([]);

  protected readonly today = new Date().toLocaleDateString(undefined, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  });

  protected readonly products = computed(() =>
    [...new Set(this.allBoards().map((b) => b.product).filter(Boolean))].sort(),
  );

  protected readonly sprints = computed(() =>
    [...new Set(this.allBoards().map((b) => b.sprint).filter((s): s is string => !!s))].sort(),
  );

  protected readonly squads = computed(() =>
    [...new Set(this.allBoards().map((b) => b.squadName).filter(Boolean))].sort(),
  );

  /** The headline strip. Every figure is real; none is derived from a guess. */
  protected readonly kpis = computed<Kpi[]>(() => {
    const s = this.summary();
    if (!s) return [];

    const statusCount = (status: number) =>
      s.statusBreakdown.find((b) => b.status === status)?.count ?? 0;

    // Matched by label rather than a hardcoded number, because roles are configurable.
    const roleCount = (matches: (label: string) => boolean) =>
      s.roleTotals.filter((r) => matches(r.label.toLowerCase())).reduce((n, r) => n + r.count, 0);

    return [
      { label: 'Total squads', value: String(s.headline.squadCount) },
      { label: 'Active projects', value: String(s.headline.totalBoards) },
      { label: 'Average progress', value: `${s.headline.averageProgressPercent}%` },
      { label: 'Developers', value: String(roleCount((l) => l.includes('develop'))) },
      { label: 'QA', value: String(roleCount((l) => l.includes('qa') || l.includes('quality'))) },
      { label: 'At risk', value: String(statusCount(1)), tone: 'warn' },
      { label: 'Blocked', value: String(statusCount(2)), tone: 'bad' },
    ];
  });

  protected readonly isEmpty = computed(() => !this.loading() && this.items().length === 0);

  /** True when the list is empty only because of the filters, not because there are none. */
  protected readonly isFiltered = computed(
    () =>
      this.search().trim().length > 0 ||
      this.statusFilter() !== null ||
      this.productFilter() !== null ||
      this.sprintFilter() !== null ||
      this.squadFilter() !== null,
  );

  constructor() {
    this.reload();
    this.loadHeadline();
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
          // Product, sprint and squad have no server-side filter, so they are applied
          // here over what the server returned.
          this.items.set(result.items.filter((b) => this.matchesLocalFilters(b)));
          this.totalCount.set(result.totalCount);

          // Remember the unfiltered set the first time, to populate the dropdowns.
          if (!this.isFiltered()) {
            this.allBoards.set(result.items);
          }

          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load boards.');
          this.loading.set(false);
        },
      });
  }

  private matchesLocalFilters(board: BoardSummary): boolean {
    const product = this.productFilter();
    const sprint = this.sprintFilter();
    const squad = this.squadFilter();

    return (
      (product === null || board.product === product) &&
      (sprint === null || board.sprint === sprint) &&
      (squad === null || board.squadName === squad)
    );
  }

  private loadHeadline(): void {
    this.http.get<PortfolioSummary>(`${this.baseUrl}/portfolio/summary`).subscribe({
      // The strip is a summary, not the page: if it fails the boards still render.
      next: (summary) => this.summary.set(summary),
      error: () => this.summary.set(null),
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

  protected onProductFilter(value: string): void {
    this.productFilter.set(value === '' ? null : value);
    this.reload();
  }

  protected onSprintFilter(value: string): void {
    this.sprintFilter.set(value === '' ? null : value);
    this.reload();
  }

  protected onSquadFilter(value: string): void {
    this.squadFilter.set(value === '' ? null : value);
    this.reload();
  }

  protected toggleMoreFilters(): void {
    this.showMoreFilters.update((v) => !v);
  }

  protected resetFilters(): void {
    this.search.set('');
    this.statusFilter.set(null);
    this.productFilter.set(null);
    this.sprintFilter.set(null);
    this.squadFilter.set(null);
    this.reload();
  }

  /** Everything a card needs to flag, in the order a reader should see it. */
  protected concerns(board: BoardSummary): { text: string; blocking: boolean }[] {
    const out: { text: string; blocking: boolean }[] = [];

    if (board.blockerNote) out.push({ text: board.blockerNote, blocking: true });
    if (board.riskNote) out.push({ text: board.riskNote, blocking: false });

    for (const warning of board.warnings) {
      out.push({ text: warning, blocking: false });
    }

    return out;
  }

  protected overflowCount(board: BoardSummary): number {
    return Math.max(0, board.memberCount - board.faces.length);
  }

  protected formatTarget(value: string | null): string | null {
    if (!value) return null;

    return new Date(value).toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
  }

  protected formatUpdated(value: string): string {
    return new Date(value).toLocaleDateString(undefined, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
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
