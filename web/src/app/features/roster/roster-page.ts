import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { PeopleService, type PersonRequest } from '../../core/services/people.service';
import { MetadataService } from '../../core/services/metadata.service';
import type { Person, Role } from '../../core/models/board.models';

/** Blank row state for the "add a person" form. */
const emptyDraft = (): PersonRequest => ({
  fullName: '',
  defaultRole: 2 as Role,
  defaultDetail: null,
  email: null,
  avatarColorOverride: null,
});

/**
 * The org-wide roster: the list people are picked from, so squad membership never
 * depends on retyping a name. Deactivated people stay listed (behind a toggle) because
 * deletion is soft and reversible.
 */
@Component({
  selector: 'app-roster-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './roster-page.html',
  styleUrl: './roster-page.scss',
})
export class RosterPage {
  private readonly people = inject(PeopleService);
  private readonly metadata = inject(MetadataService);

  protected readonly roles = this.metadata.roles;

  protected readonly items = signal<Person[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly includeInactive = signal(false);
  protected readonly busyId = signal<string | null>(null);

  /** Id of the row being edited inline, if any. */
  protected readonly editingId = signal<string | null>(null);
  protected readonly draft = signal<PersonRequest>(emptyDraft());
  protected readonly adding = signal(false);

  protected readonly activeCount = computed(() => this.items().filter((p) => p.isActive).length);

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);

    this.people
      .list({
        q: this.search().trim() || undefined,
        includeInactive: this.includeInactive(),
        pageSize: 200,
      })
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load the roster.');
          this.loading.set(false);
        },
      });
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.reload();
  }

  protected toggleInactive(value: boolean): void {
    this.includeInactive.set(value);
    this.reload();
  }

  // ---- add ----

  protected startAdd(): void {
    this.adding.set(true);
    this.editingId.set(null);
    this.draft.set(emptyDraft());
  }

  protected cancelEdit(): void {
    this.adding.set(false);
    this.editingId.set(null);
    this.draft.set(emptyDraft());
    this.error.set(null);
  }

  protected updateDraft<K extends keyof PersonRequest>(key: K, value: PersonRequest[K]): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  protected setDraftRole(value: string): void {
    this.updateDraft('defaultRole', Number(value) as Role);
  }

  protected saveDraft(): void {
    const draft = this.draft();
    if (!draft.fullName.trim()) return;

    const payload: PersonRequest = {
      ...draft,
      fullName: draft.fullName.trim(),
      defaultDetail: draft.defaultDetail?.trim() || null,
      email: draft.email?.trim() || null,
    };

    const editingId = this.editingId();
    const request$ = editingId
      ? this.people.update(editingId, payload)
      : this.people.create(payload);

    request$.subscribe({
      next: () => {
        this.cancelEdit();
        this.reload();
      },
      error: (err) => this.error.set(readProblem(err) ?? 'Could not save that person.'),
    });
  }

  // ---- edit ----

  protected startEdit(person: Person): void {
    this.adding.set(false);
    this.editingId.set(person.id);
    this.draft.set({
      fullName: person.fullName,
      defaultRole: person.defaultRole,
      defaultDetail: person.defaultDetail,
      email: person.email,
      avatarColorOverride: person.avatarColorOverride,
    });
  }

  // ---- soft delete ----

  protected deactivate(person: Person): void {
    this.busyId.set(person.id);
    this.people.deactivate(person.id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.reload();
      },
      error: () => {
        this.busyId.set(null);
        this.error.set(`Could not deactivate ${person.fullName}.`);
      },
    });
  }

  protected reactivate(person: Person): void {
    this.busyId.set(person.id);
    this.people.reactivate(person.id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.reload();
      },
      error: () => {
        this.busyId.set(null);
        this.error.set(`Could not reactivate ${person.fullName}.`);
      },
    });
  }
}

function readProblem(err: unknown): string | null {
  const problem = (err as { error?: { detail?: string; title?: string } })?.error;
  return problem?.detail ?? problem?.title ?? null;
}
