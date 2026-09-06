import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { API_BASE_URL } from '../../core/api.config';
import { BarChart, type BarDatum } from '../../shared/charts/bar-chart';
import { LineChart, type LineSeries } from '../../shared/charts/line-chart';
import { StackedBarChart, type StackRow } from '../../shared/charts/stacked-bar-chart';

interface Coverage {
  recordedProgressChanges: number;
  weeksCovered: number;
  hasRealHistory: boolean;
  note: string;
}

interface SquadComparison {
  squadName: string;
  boardCount: number;
  memberCount: number;
  averageProgressPercent: number;
  onTrack: number;
  atRisk: number;
  blocked: number;
  inReview: number;
  delivered: number;
  notableRiskCount: number;
  totalAllocationPercent: number;
}

interface WeekPoint {
  weekStart: string;
  label: string;
  averageProgressPercent: number;
  boardsTracked: number;
}

interface Series {
  name: string;
  color: string;
  values: (number | null)[];
}

interface MemberLoad {
  personId: string;
  fullName: string;
  initials: string;
  color: string;
  roles: string[];
  squadCount: number;
  squads: string[];
  totalAllocationPercent: number;
  allocationKnown: boolean;
  averageBoardProgressPercent: number;
  boardsAtRisk: number;
  boardsBlocked: number;
  recordedEdits: number;
}

interface RoleMix {
  squadName: string;
  roles: { label: string; color: string; count: number }[];
}

interface Analytics {
  squads: SquadComparison[];
  weeks: WeekPoint[];
  squadTrends: Series[];
  members: MemberLoad[];
  roleMix: RoleMix[];
  coverage: Coverage;
}

/**
 * Comparative analytics: squads beside each other, progress week by week, and how loaded
 * each person is.
 *
 * The page states what it is drawn from. The weekly trend is replayed from recorded
 * progress changes, so a portfolio that has only been imported gets a flat line and a
 * banner saying so, rather than a confident curve through a single measurement.
 *
 * The people section is titled workload rather than performance on purpose: the app knows
 * who is on which squad at what allocation, and nothing about what any individual
 * delivered. Every column here is load or exposure, and the page says which.
 */
@Component({
  selector: 'app-analytics-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BarChart, LineChart, StackedBarChart],
  templateUrl: './analytics-page.html',
  styleUrl: './analytics-page.scss',
})
export class AnalyticsPage {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  protected readonly data = signal<Analytics | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly weeks = signal(12);

  /** Every chart has a table behind it, for screen readers and for copying figures out. */
  protected readonly showTables = signal(false);

  protected readonly isEmpty = computed(() => (this.data()?.squads.length ?? 0) === 0);

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);

    this.http.get<Analytics>(`${this.baseUrl}/analytics?weeks=${this.weeks()}`).subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load the analytics.');
        this.loading.set(false);
      },
    });
  }

  protected setWeeks(value: string): void {
    this.weeks.set(Number(value));
    this.reload();
  }

  protected toggleTables(): void {
    this.showTables.update((v) => !v);
  }

  // ------------------------------------------------------------------
  // Trend
  // ------------------------------------------------------------------

  protected readonly weekLabels = computed(() => this.data()?.weeks.map((w) => w.label) ?? []);

  /**
   * A week with nothing tracked is a gap, not a zero. Sending 0 would draw a line down to
   * the axis and claim the portfolio was at zero percent in a week it did not yet exist.
   */
  protected readonly portfolioTrend = computed<LineSeries[]>(() => {
    const weeks = this.data()?.weeks ?? [];
    if (weeks.length === 0) return [];

    return [
      {
        name: 'Portfolio average',
        color: '#0F766E',
        values: weeks.map((w) => (w.boardsTracked === 0 ? null : w.averageProgressPercent)),
      },
    ];
  });

  protected readonly squadTrend = computed<LineSeries[]>(
    () =>
      this.data()?.squadTrends.map((s) => ({
        name: s.name,
        color: s.color,
        values: s.values,
      })) ?? [],
  );

  // ------------------------------------------------------------------
  // Squad comparison
  // ------------------------------------------------------------------

  /** Magnitude, so one hue — a rainbow here would imply categories that do not exist. */
  protected readonly progressBySquad = computed<BarDatum[]>(
    () =>
      this.data()?.squads.map((s) => ({
        label: s.squadName,
        value: s.averageProgressPercent,
      })) ?? [],
  );

  protected readonly boardsBySquad = computed<BarDatum[]>(
    () =>
      this.data()?.squads.map((s) => ({
        label: s.squadName,
        value: s.boardCount,
      })) ?? [],
  );

  protected readonly healthBySquad = computed<StackRow[]>(
    () =>
      this.data()?.squads.map((s) => ({
        name: s.squadName,
        total: s.boardCount,
        segments: [
          { label: 'On Track', color: '#34D399', count: s.onTrack },
          { label: 'In Review', color: '#60A5FA', count: s.inReview },
          { label: 'At Risk', color: '#FBBF24', count: s.atRisk },
          { label: 'Blocked', color: '#F87171', count: s.blocked },
          // Delivered shares the teal family with On Track, so it carries a hatch as
          // well as its label — the two must not read as one slice.
          { label: 'Delivered', color: '#2DD4BF', count: s.delivered, texture: true },
        ],
      })) ?? [],
  );

  protected readonly capacityBySquad = computed<StackRow[]>(
    () =>
      this.data()?.roleMix.map((s) => ({
        name: s.squadName,
        total: s.roles.reduce((n, r) => n + r.count, 0),
        segments: s.roles.map((r) => ({ label: r.label, color: r.color, count: r.count })),
      })) ?? [],
  );

  // ------------------------------------------------------------------
  // People
  // ------------------------------------------------------------------

  protected readonly members = computed(() => this.data()?.members ?? []);

  protected readonly allocationByMember = computed<BarDatum[]>(
    () =>
      this.members()
        .filter((m) => m.totalAllocationPercent > 0)
        .map((m) => ({
          label: m.fullName,
          value: m.totalAllocationPercent,
        })),
  );

  /** Over 100% across squads is the one number here that is actionable on its own. */
  protected readonly overCommitted = computed(() =>
    this.members().filter((m) => m.totalAllocationPercent > 100),
  );

  protected readonly maxAllocation = computed(() =>
    Math.max(100, ...this.members().map((m) => m.totalAllocationPercent)),
  );

  protected readonly anyAllocationRecorded = computed(() =>
    this.members().some((m) => m.totalAllocationPercent > 0),
  );
}
