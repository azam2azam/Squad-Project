import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StaffService, type StaffSchedule } from '../../core/services/staff.service';

/**
 * The team schedule: everyone down the side, weeks across, and what is left of each
 * person's week.
 *
 * The cell shows **free capacity**, not committed, because the question this page exists
 * to answer is "who can take this on". Over-commitment is the one thing that gets a colour
 * of its own — it is the only value here that is actionable on sight.
 */
@Component({
  selector: 'app-staff-schedule-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './staff-schedule-page.html',
  styleUrl: './staff-schedule-page.scss',
})
export class StaffSchedulePage {
  private readonly staff = inject(StaffService);

  protected readonly data = signal<StaffSchedule | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly weeks = signal(8);
  protected readonly hideFullyFree = signal(false);

  protected readonly people = computed(() => {
    const rows = this.data()?.people ?? [];
    if (!this.hideFullyFree()) return rows;

    // "Committed somewhere" is the useful filter when planning around a busy team.
    return rows.filter((p) => p.weeks.some((w) => w.committedPercent > 0));
  });

  protected readonly overCommitted = computed(() =>
    (this.data()?.people ?? []).filter((p) => p.weeks.some((w) => w.overCommitted)),
  );

  protected readonly totals = computed(() => {
    const rows = this.data()?.people ?? [];
    const columns = this.data()?.weeks.length ?? 0;
    if (columns === 0) return [];

    // Team-wide free capacity per week, which is what a lead reads first.
    return Array.from({ length: columns }, (_, i) => {
      const free = rows.reduce((sum, p) => sum + Math.max(0, p.weeks[i]?.freePercent ?? 0), 0);
      return Math.round(free / 100);
    });
  });

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.data.set(await this.staff.schedule(this.weeks()));
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not load the schedule.'));
    } finally {
      this.loading.set(false);
    }
  }

  protected setWeeks(value: string): void {
    this.weeks.set(Number(value));
    void this.reload();
  }

  protected toggleHideFree(value: boolean): void {
    this.hideFullyFree.set(value);
  }

  /**
   * Free capacity as a bar width. Over-commitment is drawn full and red rather than
   * inverted, so a squeezed week never looks like a roomy one.
   */
  protected barWidth(free: number): number {
    return Math.max(0, Math.min(100, free));
  }

  protected cellTitle(boards: string[], away: string[]): string {
    const parts: string[] = [];
    if (boards.length) parts.push(boards.join(', '));
    if (away.length) parts.push('Away: ' + away.join(', '));
    return parts.join(' · ') || 'Nothing scheduled';
  }

  private messageFrom(err: unknown, fallback: string): string {
    const body = (err as { error?: { detail?: string; title?: string } })?.error;
    return body?.detail ?? body?.title ?? fallback;
  }
}
