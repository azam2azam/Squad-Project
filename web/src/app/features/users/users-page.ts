import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { PeopleService } from '../../core/services/people.service';
import {
  MIN_PASSWORD_LENGTH,
  UsersService,
  type AccessLevel,
  type AppUser,
} from '../../core/services/users.service';
import type { Person } from '../../core/models/board.models';

interface Draft {
  email: string;
  displayName: string;
  role: number;
  password: string;
  personId: string | null;
}

const emptyDraft = (): Draft => ({
  email: '',
  displayName: '',
  role: 1, // Product Owner: the role most people being added will need.
  password: '',
  personId: null,
});

/**
 * Who can sign in.
 *
 * Deliberately separate from the Roster: the roster is who appears on a slide, this is
 * who has access. Most roster members never log in, and an admin need not be on a squad —
 * so an account can optionally be linked to a roster person, but is not required to be.
 *
 * Accounts are deactivated rather than deleted. Boards and audit entries record who did
 * what, and deleting an account would leave that history pointing at nobody.
 */
@Component({
  selector: 'app-users-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './users-page.html',
  styleUrl: './users-page.scss',
})
export class UsersPage {
  private readonly users = inject(UsersService);
  private readonly people = inject(PeopleService);
  private readonly auth = inject(AuthService);

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  protected readonly items = signal<AppUser[]>([]);
  protected readonly levels = signal<AccessLevel[]>([]);
  protected readonly roster = signal<Person[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly busyId = signal<string | null>(null);
  protected readonly includeInactive = signal(false);

  protected readonly adding = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly draft = signal<Draft>(emptyDraft());

  /** Id of the account whose password is being reset, if any. */
  protected readonly resettingId = signal<string | null>(null);
  protected readonly newPassword = signal('');

  protected readonly activeCount = computed(() => this.items().filter((u) => u.isActive).length);
  protected readonly adminCount = computed(
    () => this.items().filter((u) => u.isActive && u.role === 2).length,
  );

  /** Your own account: the UI hides the actions the server would refuse anyway. */
  protected readonly myId = computed(() => this.auth.user()?.id ?? null);

  protected readonly canSaveDraft = computed(() => {
    const d = this.draft();
    if (!d.displayName.trim()) return false;

    if (this.editingId()) return true;

    return (
      d.email.trim().includes('@') && d.password.trim().length >= this.minPasswordLength
    );
  });

  constructor() {
    void this.reload();
    void this.loadReference();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.items.set(await this.users.list(this.includeInactive()));
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not load the user list.'));
    } finally {
      this.loading.set(false);
    }
  }

  private async loadReference(): Promise<void> {
    try {
      this.levels.set(await this.users.accessLevels());
    } catch {
      // The list still works without descriptions; the role column falls back to labels.
    }

    try {
      const page = await firstValueFrom(this.people.list({ pageSize: 200 }));
      this.roster.set(page.items);
    } catch {
      // Linking to the roster is optional, so a roster failure must not block user admin.
    }
  }

  protected toggleInactive(value: boolean): void {
    this.includeInactive.set(value);
    void this.reload();
  }

  protected startAdd(): void {
    this.draft.set(emptyDraft());
    this.editingId.set(null);
    this.adding.set(true);
    this.notice.set(null);
  }

  protected startEdit(user: AppUser): void {
    this.draft.set({
      email: user.email,
      displayName: user.displayName,
      role: user.role,
      password: '',
      personId: user.personId,
    });
    this.editingId.set(user.id);
    this.adding.set(false);
    this.notice.set(null);
  }

  protected cancelEdit(): void {
    this.adding.set(false);
    this.editingId.set(null);
    this.draft.set(emptyDraft());
  }

  protected updateDraft<K extends keyof Draft>(key: K, value: Draft[K]): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  protected setDraftRole(value: string): void {
    this.updateDraft('role', Number(value));
  }

  protected setDraftPerson(value: string): void {
    this.updateDraft('personId', value === '' ? null : value);
  }

  protected async saveDraft(): Promise<void> {
    if (!this.canSaveDraft()) return;

    const d = this.draft();
    this.error.set(null);

    try {
      const editing = this.editingId();

      if (editing) {
        await this.users.update(editing, {
          displayName: d.displayName.trim(),
          role: d.role,
          personId: d.personId,
        });
        this.notice.set(`${d.displayName.trim()} updated.`);
      } else {
        await this.users.create({
          email: d.email.trim(),
          displayName: d.displayName.trim(),
          role: d.role,
          password: d.password,
          personId: d.personId,
        });
        this.notice.set(
          `${d.displayName.trim()} can now sign in. Ask them to change the password you set.`,
        );
      }

      this.cancelEdit();
      await this.reload();
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not save the account.'));
    }
  }

  protected async setActive(user: AppUser, isActive: boolean): Promise<void> {
    this.busyId.set(user.id);
    this.error.set(null);

    try {
      await this.users.setActive(user.id, isActive);
      this.notice.set(
        isActive ? `${user.displayName} can sign in again.` : `${user.displayName} deactivated.`,
      );
      await this.reload();
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not change the account.'));
    } finally {
      this.busyId.set(null);
    }
  }

  protected startReset(user: AppUser): void {
    this.resettingId.set(user.id);
    this.newPassword.set('');
    this.notice.set(null);
    this.error.set(null);
  }

  protected cancelReset(): void {
    this.resettingId.set(null);
    this.newPassword.set('');
  }

  protected async confirmReset(user: AppUser): Promise<void> {
    if (this.newPassword().length < this.minPasswordLength) return;

    this.busyId.set(user.id);
    this.error.set(null);

    try {
      await this.users.resetPassword(user.id, this.newPassword());
      this.cancelReset();
      this.notice.set(
        `Password set for ${user.displayName}. They are signed out, and should change it.`,
      );
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not set the password.'));
    } finally {
      this.busyId.set(null);
    }
  }

  protected levelDescription(role: number): string {
    return this.levels().find((l) => l.value === role)?.description ?? '';
  }

  protected formatDate(value: string | null): string {
    return value ? new Date(value).toLocaleDateString() : 'never';
  }

  /** Surfaces the server's own message — the rules it enforces are worth reading. */
  private messageFrom(err: unknown, fallback: string): string {
    const body = (err as { error?: { detail?: string; title?: string } })?.error;
    return body?.detail ?? body?.title ?? fallback;
  }
}
