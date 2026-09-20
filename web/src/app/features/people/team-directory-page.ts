import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { ProfilesService, type DirectoryEntry } from '../../core/services/profiles.service';

/**
 * The team, as cards you can look through.
 *
 * Deliberately not the roster screen. That one is an administrator's table for maintaining
 * records — names, default roles, active flags. This is for everybody else: who is here,
 * what they work with, and who to ask about FHIR. The search covers skills for exactly that
 * reason.
 */
@Component({
  selector: 'app-team-directory-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './team-directory-page.html',
  styleUrl: './team-directory-page.scss',
})
export class TeamDirectoryPage {
  private readonly profiles = inject(ProfilesService);
  private readonly auth = inject(AuthService);

  protected readonly people = signal<DirectoryEntry[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly includeInactive = signal(false);

  protected readonly isAdmin = this.auth.isAdmin;

  /** The signed-in person's own roster entry, when their account is linked to one. */
  protected readonly myPersonId = computed(() => this.auth.user()?.personId ?? null);

  protected readonly withPhotos = computed(() => this.people().filter((p) => p.hasPhoto).length);

  protected readonly withSkills = computed(() => this.people().filter((p) => p.skills.length > 0).length);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.people.set(await this.profiles.directory(this.search(), this.includeInactive()));
      this.error.set(null);
    } catch (err) {
      const problem = (err as { error?: { detail?: string; title?: string } })?.error;
      this.error.set(problem?.detail ?? problem?.title ?? 'Could not load the team.');
    } finally {
      this.loading.set(false);
    }
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    void this.load();
  }

  protected toggleInactive(): void {
    this.includeInactive.set(!this.includeInactive());
    void this.load();
  }

  protected photoUrl(person: DirectoryEntry): string {
    return this.profiles.photoUrl(person.id, person.photoVersion);
  }
}
