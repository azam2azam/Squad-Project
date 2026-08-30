import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { API_BASE_URL } from '../../core/api.config';
import { BarChart, type BarDatum } from '../../shared/charts/bar-chart';
import { DonutChart, type DonutSlice } from '../../shared/charts/donut-chart';

interface PortfolioSummary {
  headline: {
    totalBoards: number;
    totalPeople: number;
    averageProgressPercent: number;
    onTrackPercent: number;
    squadCount: number;
    boardsNeedingAttention: number;
  };
  statusBreakdown: {
    status: number;
    label: string;
    color: string;
    count: number;
    percent: number;
    needsTexture: boolean;
  }[];
  squads: {
    squadName: string;
    boardCount: number;
    memberCount: number;
    averageProgressPercent: number;
    onTrackCount: number;
    atRiskCount: number;
    blockedCount: number;
    deliveredCount: number;
  }[];
  riskRegister: {
    boardId: string;
    title: string;
    squadName: string;
    level: number;
    levelLabel: string;
    levelColor: string;
    riskNote: string | null;
    status: number;
    statusLabel: string;
    progressPercent: number;
  }[];
  roleTotals: { role: number; label: string; color: string; count: number }[];
  needsAttention: {
    boardId: string;
    title: string;
    squadName: string;
    reasons: string[];
  }[];
}

/**
 * The delivery-lead landing page: portfolio health at a glance, then the things that
 * need looking at.
 *
 * Every number here is computed server-side in one query, so the headline figures
 * cannot drift from the boards they summarise.
 */
@Component({
  selector: 'app-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DonutChart, BarChart],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss',
})
export class DashboardPage {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  protected readonly summary = signal<PortfolioSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly statusSlices = computed<DonutSlice[]>(
    () =>
      this.summary()?.statusBreakdown.map((s) => ({
        label: s.label,
        value: s.count,
        color: s.color,
        // On Track and Delivered are near-identical hues; the server flags which
        // slice carries a texture so identity never rests on colour alone.
        texture: s.needsTexture,
      })) ?? [],
  );

  protected readonly squadProgress = computed<BarDatum[]>(
    () =>
      this.summary()?.squads.map((s) => ({
        label: s.squadName,
        value: s.averageProgressPercent,
        detail: `${s.boardCount} ${s.boardCount === 1 ? 'board' : 'boards'} · ${s.memberCount} people`,
      })) ?? [],
  );

  protected readonly roleTotals = computed<BarDatum[]>(
    () =>
      this.summary()?.roleTotals.map((r) => ({
        label: r.label,
        value: r.count,
      })) ?? [],
  );

  protected readonly isEmpty = computed(
    () => !this.loading() && (this.summary()?.headline.totalBoards ?? 0) === 0,
  );

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.http.get<PortfolioSummary>(`${this.baseUrl}/portfolio/summary`).subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load the portfolio summary.');
        this.loading.set(false);
      },
    });
  }
}
