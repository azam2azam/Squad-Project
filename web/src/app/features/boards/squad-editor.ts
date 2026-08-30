import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { PeopleService } from '../../core/services/people.service';
import { MetadataService } from '../../core/services/metadata.service';
import type { Person, Role, SquadMember } from '../../core/models/board.models';

/**
 * Squad membership editing, following the prototype's "Add squad member" panel:
 * a name field, role pills, an optional detail, then the current roster list with
 * remove buttons.
 *
 * The name field is a roster typeahead rather than free text — people are picked,
 * not retyped (spec section 1). Typing a name nobody matches still works: it
 * quick-creates the person, so they join the roster and are reusable next time.
 */
@Component({
  selector: 'app-squad-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './squad-editor.html',
  styleUrl: './squad-editor.scss',
})
export class SquadEditor {
  readonly boardId = input.required<string>();
  readonly members = input.required<readonly SquadMember[]>();

  /** Raised whenever membership changed, so the parent can refetch the board. */
  readonly changed = output<void>();

  private readonly people = inject(PeopleService);
  private readonly metadata = inject(MetadataService);

  protected readonly roles = this.metadata.roles;

  protected readonly nameQuery = signal('');
  protected readonly detail = signal('');
  protected readonly pendingRole = signal<Role>(2 as Role); // Developer, as in the prototype
  protected readonly suggestions = signal<Person[]>([]);
  protected readonly selectedPerson = signal<Person | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Ids already on this squad — they are filtered out of the picker. */
  private readonly memberPersonIds = computed(() => new Set(this.members().map((m) => m.personId)));

  protected readonly visibleSuggestions = computed(() =>
    this.suggestions().filter((p) => !this.memberPersonIds().has(p.id)),
  );

  protected readonly canAdd = computed(
    () => this.nameQuery().trim().length > 0 || this.selectedPerson() !== null,
  );

  protected onNameInput(value: string): void {
    this.nameQuery.set(value);
    this.selectedPerson.set(null);
    this.error.set(null);

    const term = value.trim();
    if (term.length < 2) {
      this.suggestions.set([]);
      return;
    }

    this.people.list({ q: term, pageSize: 6 }).subscribe({
      next: (result) => this.suggestions.set(result.items),
      error: () => this.suggestions.set([]),
    });
  }

  protected pick(person: Person): void {
    this.selectedPerson.set(person);
    this.nameQuery.set(person.fullName);
    this.pendingRole.set(person.defaultRole);
    if (person.defaultDetail) {
      this.detail.set(person.defaultDetail);
    }
    this.suggestions.set([]);
  }

  protected setRole(role: Role): void {
    this.pendingRole.set(role);
  }

  protected onDetailInput(value: string): void {
    this.detail.set(value);
  }

  protected add(): void {
    if (!this.canAdd() || this.busy()) return;

    const picked = this.selectedPerson();
    const name = this.nameQuery().trim();
    const detail = this.detail().trim() || null;

    this.busy.set(true);
    this.error.set(null);

    this.people
      .addMember(this.boardId(), {
        // A picked person is referenced by id; anything else quick-creates a
        // roster entry so the name is reusable on the next board.
        personId: picked ? picked.id : null,
        newPerson: picked
          ? null
          : { fullName: name, defaultRole: this.pendingRole(), defaultDetail: detail, email: null },
        role: this.pendingRole(),
        detail,
        allocationPercent: null,
      })
      .subscribe({
        next: () => {
          this.reset();
          this.busy.set(false);
          this.changed.emit();
        },
        error: (err) => {
          this.busy.set(false);
          this.error.set(readProblem(err) ?? 'Could not add that person.');
        },
      });
  }

  protected changeRole(member: SquadMember, value: string): void {
    this.people
      .updateMember(member.id, {
        role: Number(value) as Role,
        detail: member.detail,
        allocationPercent: member.allocationPercent,
      })
      .subscribe({
        next: () => this.changed.emit(),
        error: (err) => this.error.set(readProblem(err) ?? 'Could not update that member.'),
      });
  }

  protected remove(member: SquadMember): void {
    this.people.removeMember(member.id).subscribe({
      next: () => this.changed.emit(),
      error: (err) => this.error.set(readProblem(err) ?? 'Could not remove that member.'),
    });
  }

  protected onNameKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      this.add();
    }
  }

  private reset(): void {
    this.nameQuery.set('');
    this.detail.set('');
    this.selectedPerson.set(null);
    this.suggestions.set([]);
  }
}

function readProblem(err: unknown): string | null {
  const problem = (err as { error?: { detail?: string; title?: string } })?.error;
  return problem?.detail ?? problem?.title ?? null;
}
