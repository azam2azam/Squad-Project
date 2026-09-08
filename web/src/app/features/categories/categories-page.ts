import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  CategoriesService,
  type BoardCategory,
} from '../../core/services/categories.service';

interface Draft {
  name: string;
  description: string;
  color: string;
}

/** Distinct hues so two programmes never arrive looking alike. */
const SUGGESTED_COLORS = [
  '#2563EB',
  '#7C3AED',
  '#0891B2',
  '#B45309',
  '#4D7C0F',
  '#DB2777',
];

const emptyDraft = (): Draft => ({ name: '', description: '', color: SUGGESTED_COLORS[0] });

/**
 * Programmes above the boards — VIDA 4, AI, and so on.
 *
 * This is a level above a board's Product: Product names the module a board covers
 * (Discharge, Invoice), a category collects many of those under one programme.
 *
 * Retiring is soft. A retired category leaves the pickers but boards already in it keep
 * their grouping, so retiring one never silently reshuffles the portfolio.
 */
@Component({
  selector: 'app-categories-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './categories-page.html',
  styleUrl: './categories-page.scss',
})
export class CategoriesPage {
  private readonly categories = inject(CategoriesService);

  protected readonly suggestedColors = SUGGESTED_COLORS;

  protected readonly items = signal<BoardCategory[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly busyId = signal<string | null>(null);
  protected readonly includeInactive = signal(false);

  protected readonly adding = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly draft = signal<Draft>(emptyDraft());

  protected readonly categorisedBoards = computed(() =>
    this.items().reduce((n, c) => n + c.boardCount, 0),
  );

  protected readonly colorProblem = computed(() =>
    /^#[0-9A-Fa-f]{6}$/.test(this.draft().color.trim())
      ? null
      : 'Use a six-digit hex colour, for example #2563EB.',
  );

  protected readonly canSave = computed(
    () => this.draft().name.trim().length > 0 && !this.colorProblem(),
  );

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.items.set(await this.categories.list(this.includeInactive()));
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not load the categories.'));
    } finally {
      this.loading.set(false);
    }
  }

  protected toggleInactive(value: boolean): void {
    this.includeInactive.set(value);
    void this.reload();
  }

  protected startAdd(): void {
    // Offer a colour nobody is using, so two programmes do not arrive looking alike.
    const used = new Set(this.items().map((c) => c.color.toUpperCase()));
    const free = SUGGESTED_COLORS.find((c) => !used.has(c.toUpperCase())) ?? SUGGESTED_COLORS[0];

    this.draft.set({ ...emptyDraft(), color: free });
    this.editingId.set(null);
    this.adding.set(true);
    this.notice.set(null);
  }

  protected startEdit(category: BoardCategory): void {
    this.draft.set({
      name: category.name,
      description: category.description ?? '',
      color: category.color,
    });
    this.editingId.set(category.id);
    this.adding.set(false);
    this.notice.set(null);
  }

  protected cancel(): void {
    this.adding.set(false);
    this.editingId.set(null);
    this.draft.set(emptyDraft());
  }

  protected updateDraft<K extends keyof Draft>(key: K, value: Draft[K]): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  protected async save(): Promise<void> {
    if (!this.canSave()) return;

    const d = this.draft();
    this.error.set(null);

    try {
      const editing = this.editingId();

      if (editing) {
        const existing = this.items().find((c) => c.id === editing);
        await this.categories.update(editing, {
          name: d.name.trim(),
          description: d.description.trim() || null,
          color: d.color.trim(),
          orderIndex: existing?.orderIndex ?? 0,
        });
        this.notice.set(`${d.name.trim()} updated.`);
      } else {
        await this.categories.create({
          name: d.name.trim(),
          description: d.description.trim() || null,
          color: d.color.trim(),
        });
        this.notice.set(`${d.name.trim()} is ready — assign boards to it from a board or Excel.`);
      }

      this.cancel();
      await this.reload();
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not save the category.'));
    }
  }

  protected async setActive(category: BoardCategory, isActive: boolean): Promise<void> {
    this.busyId.set(category.id);
    this.error.set(null);

    try {
      await this.categories.setActive(category.id, isActive);
      this.notice.set(
        isActive
          ? `${category.name} is back in the pickers.`
          : `${category.name} retired. Its ${category.boardCount} board(s) keep the grouping.`,
      );
      await this.reload();
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not change the category.'));
    } finally {
      this.busyId.set(null);
    }
  }

  private messageFrom(err: unknown, fallback: string): string {
    const body = (err as { error?: { detail?: string; title?: string } })?.error;
    return body?.detail ?? body?.title ?? fallback;
  }
}
