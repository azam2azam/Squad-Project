import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { StaffService, type StaffOption, type WorkItem } from '../../core/services/staff.service';
import type { SquadMember } from '../../core/models/board.models';

interface WorkItemDraft {
  id: string | null;
  title: string;
  /** Empty string is unassigned, which is a real and useful state. */
  personId: string;
  status: number;
  plannedStart: string;
  plannedEnd: string;
  /** Held as a string because an empty number input is not zero hours. */
  effortHours: string;
  detail: string;
}

const emptyDraft = (): WorkItemDraft => ({
  id: null,
  title: '',
  personId: '',
  status: 0,
  plannedStart: '',
  plannedEnd: '',
  effortHours: '',
  detail: '',
});

/**
 * The tasks on this board — what the schedule and the person profiles are counting.
 *
 * Assignment is optional on purpose: an unassigned task is the one a lead is looking
 * for, so it has to be expressible rather than forced onto whoever is nearest. The
 * assignee list is the board's own squad, because assigning work to somebody who is not
 * on the board is how a roster stops meaning anything.
 */
@Component({
  selector: 'app-work-items-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink],
  templateUrl: './work-items-editor.html',
  styleUrl: './work-items-editor.scss',
})
export class WorkItemsEditor {
  readonly boardId = input.required<string>();
  readonly members = input.required<readonly SquadMember[]>();

  /** Viewers still see the list; only the controls are withheld. */
  readonly canWrite = input<boolean>(false);

  private readonly staff = inject(StaffService);

  protected readonly items = signal<WorkItem[]>([]);
  protected readonly statuses = signal<StaffOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showDone = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly draft = signal<WorkItemDraft>(emptyDraft());

  protected readonly visible = computed(() =>
    this.showDone() ? this.items() : this.items().filter((w) => w.status !== 3),
  );

  protected readonly openCount = computed(() => this.items().filter((w) => w.status !== 3).length);
  protected readonly doneCount = computed(() => this.items().filter((w) => w.status === 3).length);
  protected readonly overdueCount = computed(() => this.items().filter((w) => w.isOverdue).length);

  protected readonly unassignedCount = computed(
    () => this.items().filter((w) => w.status !== 3 && !w.personId).length,
  );

  protected readonly canSave = computed(() => {
    const draft = this.draft();
    if (!draft.title.trim()) return false;

    // A window that ends before it starts is a typo, not a plan.
    return !draft.plannedStart || !draft.plannedEnd || draft.plannedEnd >= draft.plannedStart;
  });

  constructor() {
    // Reloads when the router swaps boards without destroying the component.
    effect(() => {
      const id = this.boardId();
      if (id) void this.reload(id);
    });

    void this.staff
      .options()
      .then((options) => this.statuses.set(options.workItemStatuses))
      .catch(() => this.statuses.set([]));
  }

  protected async reload(boardId = this.boardId()): Promise<void> {
    this.loading.set(true);

    try {
      this.items.set(await this.staff.workItems({ boardId, includeDone: true }));
      this.error.set(null);
    } catch (err) {
      this.error.set(messageFrom(err, 'Could not load the tasks on this board.'));
    } finally {
      this.loading.set(false);
    }
  }

  protected startNew(): void {
    this.draft.set(emptyDraft());
    this.formOpen.set(true);
    this.error.set(null);
  }

  protected edit(item: WorkItem): void {
    this.draft.set({
      id: item.id,
      title: item.title,
      personId: item.personId ?? '',
      status: item.status,
      plannedStart: item.plannedStart ?? '',
      plannedEnd: item.plannedEnd ?? '',
      effortHours: item.effortHours === null ? '' : String(item.effortHours),
      detail: item.detail ?? '',
    });

    this.formOpen.set(true);
    this.error.set(null);
  }

  protected cancel(): void {
    this.formOpen.set(false);
    this.draft.set(emptyDraft());
  }

  protected patch<K extends keyof WorkItemDraft>(key: K, value: WorkItemDraft[K]): void {
    this.draft.update((draft) => ({ ...draft, [key]: value }));
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (!this.canSave() || this.busy()) return;

    const effort = draft.effortHours.trim();
    const body = {
      boardId: this.boardId(),
      title: draft.title.trim(),
      personId: draft.personId || null,
      status: draft.status,
      plannedStart: draft.plannedStart || null,
      plannedEnd: draft.plannedEnd || null,
      effortHours: effort === '' ? null : Number(effort),
      detail: draft.detail.trim() || null,
    };

    this.busy.set(true);
    this.error.set(null);

    try {
      const saved = draft.id
        ? await this.staff.updateWorkItem(draft.id, body)
        : await this.staff.createWorkItem(body);

      this.merge(saved);
      this.formOpen.set(false);
      this.draft.set(emptyDraft());
    } catch (err) {
      this.error.set(messageFrom(err, 'Could not save that task.'));
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Status and assignee change straight from the list — they are the two edits people
   * make constantly, and routing them through a form is what stops a task list being
   * kept up to date.
   */
  protected setStatus(item: WorkItem, value: string): void {
    void this.quickUpdate(item, { status: Number(value) });
  }

  protected setAssignee(item: WorkItem, value: string): void {
    void this.quickUpdate(item, { personId: value || null });
  }

  private async quickUpdate(
    item: WorkItem,
    change: { status?: number; personId?: string | null },
  ): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      this.merge(
        await this.staff.updateWorkItem(item.id, {
          boardId: item.boardId,
          title: item.title,
          personId: change.personId !== undefined ? change.personId : item.personId,
          status: change.status ?? item.status,
          plannedStart: item.plannedStart,
          plannedEnd: item.plannedEnd,
          effortHours: item.effortHours,
          detail: item.detail,
        }),
      );
    } catch (err) {
      this.error.set(messageFrom(err, 'Could not update that task.'));

      // The select already moved, so refetch: leaving a value on screen the server
      // rejected is worse than a moment of flicker.
      await this.reload();
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(item: WorkItem): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await this.staff.deleteWorkItem(item.id);
      this.items.update((list) => list.filter((w) => w.id !== item.id));
      if (this.draft().id === item.id) this.cancel();
    } catch (err) {
      this.error.set(messageFrom(err, 'Could not delete that task.'));
    } finally {
      this.busy.set(false);
    }
  }

  /** Mirrors the server's ordering — open first, then by due date — rather than inventing one. */
  private merge(saved: WorkItem): void {
    this.items.update((list) => {
      const next = list.some((w) => w.id === saved.id)
        ? list.map((w) => (w.id === saved.id ? saved : w))
        : [...list, saved];

      return next.sort(
        (a, b) =>
          Number(a.status === 3) - Number(b.status === 3) ||
          (a.plannedEnd ?? '9999-12-31').localeCompare(b.plannedEnd ?? '9999-12-31') ||
          a.title.localeCompare(b.title),
      );
    });
  }
}

function messageFrom(err: unknown, fallback: string): string {
  const problem = (err as { error?: { detail?: string; title?: string } })?.error;
  return problem?.detail ?? problem?.title ?? fallback;
}
