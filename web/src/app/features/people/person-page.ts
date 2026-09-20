import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import {
  ProfilesService,
  type PersonProfileCard,
} from '../../core/services/profiles.service';

/**
 * One person's profile: their picture, a line about them, what they work with, and what
 * they have done.
 *
 * Editing is inline rather than behind a separate form page. A profile is only filled in
 * because somebody finds it easy on the afternoon they think of it, and "open the edit
 * screen, change one field, save, go back" is how a directory ends up full of empty
 * profiles.
 *
 * Every save returns the whole profile, so the page never has to guess what the server
 * made of an edit — the picture, the skills and the counts all come back in step.
 */
@Component({
  selector: 'app-person-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './person-page.html',
  styleUrl: './person-page.scss',
})
export class PersonPage {
  private readonly route = inject(ActivatedRoute);
  private readonly profiles = inject(ProfilesService);

  protected readonly profile = signal<PersonProfileCard | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly editingAbout = signal(false);
  protected readonly headlineDraft = signal('');
  protected readonly aboutDraft = signal('');
  protected readonly skillDraft = signal('');

  protected readonly addingAchievement = signal(false);
  protected readonly achievementTitle = signal('');
  protected readonly achievementDetail = signal('');
  protected readonly achievementDate = signal('');

  private readonly personId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('id'))),
    { initialValue: null },
  );

  protected readonly photoUrl = computed(() => {
    const person = this.profile();
    return person?.hasPhoto ? this.profiles.photoUrl(person.id, person.photoVersion) : null;
  });

  protected readonly canSaveAchievement = computed(() => this.achievementTitle().trim().length > 0);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    const id = this.personId();
    if (!id) return;

    this.loading.set(true);

    try {
      this.apply(await this.profiles.get(id));
      this.error.set(null);
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not load this profile.'));
    } finally {
      this.loading.set(false);
    }
  }

  private apply(profile: PersonProfileCard): void {
    this.profile.set(profile);
    this.headlineDraft.set(profile.headline ?? '');
    this.aboutDraft.set(profile.about ?? '');
  }

  protected startEditing(): void {
    this.editingAbout.set(true);
    this.notice.set(null);
  }

  protected cancelEditing(): void {
    const profile = this.profile();
    this.headlineDraft.set(profile?.headline ?? '');
    this.aboutDraft.set(profile?.about ?? '');
    this.editingAbout.set(false);
  }

  protected async saveAbout(): Promise<void> {
    const id = this.profile()?.id;
    if (!id || this.busy()) return;

    await this.run(async () => {
      this.apply(
        await this.profiles.save(id, {
          headline: this.headlineDraft().trim() || null,
          about: this.aboutDraft().trim() || null,
        }),
      );

      this.editingAbout.set(false);
      this.notice.set('Saved.');
    }, 'Could not save your profile.');
  }

  protected async addSkills(): Promise<void> {
    const id = this.profile()?.id;
    const names = this.skillDraft().trim();
    if (!id || !names || this.busy()) return;

    await this.run(async () => {
      this.apply(await this.profiles.addSkills(id, names));
      this.skillDraft.set('');
    }, 'Could not add that skill.');
  }

  protected async removeSkill(skillId: string): Promise<void> {
    const id = this.profile()?.id;
    if (!id || this.busy()) return;

    await this.run(
      async () => this.apply(await this.profiles.removeSkill(id, skillId)),
      'Could not remove that skill.',
    );
  }

  protected async addAchievement(): Promise<void> {
    const id = this.profile()?.id;
    if (!id || !this.canSaveAchievement() || this.busy()) return;

    await this.run(async () => {
      this.apply(
        await this.profiles.addAchievement(id, {
          title: this.achievementTitle().trim(),
          detail: this.achievementDetail().trim() || null,
          achievedOn: this.achievementDate() || null,
        }),
      );

      this.achievementTitle.set('');
      this.achievementDetail.set('');
      this.achievementDate.set('');
      this.addingAchievement.set(false);
    }, 'Could not add that achievement.');
  }

  protected async removeAchievement(achievementId: string): Promise<void> {
    const id = this.profile()?.id;
    if (!id || this.busy()) return;

    await this.run(
      async () => this.apply(await this.profiles.removeAchievement(id, achievementId)),
      'Could not remove that achievement.',
    );
  }

  protected async onPhotoPicked(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const id = this.profile()?.id;
    if (!file || !id) return;

    // Checked here as well as on the server so somebody picking a 12 MB photo from their
    // phone is told immediately rather than after the upload.
    if (file.size > 2 * 1024 * 1024) {
      this.error.set(
        `That picture is ${(file.size / 1024 / 1024).toFixed(1)} MB. The limit is 2 MB.`,
      );
      input.value = '';
      return;
    }

    await this.run(async () => {
      this.apply(await this.profiles.uploadPhoto(id, file));
      this.notice.set('Picture updated.');
    }, 'Could not upload that picture.');

    input.value = '';
  }

  protected async removePhoto(): Promise<void> {
    const id = this.profile()?.id;
    if (!id || this.busy()) return;

    await this.run(
      async () => this.apply(await this.profiles.removePhoto(id)),
      'Could not remove the picture.',
    );
  }

  private async run(action: () => Promise<void>, fallback: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
    } catch (err) {
      this.error.set(this.messageFrom(err, fallback));
    } finally {
      this.busy.set(false);
    }
  }

  private messageFrom(err: unknown, fallback: string): string {
    const problem = (err as { error?: { detail?: string; title?: string } })?.error;
    return problem?.detail ?? problem?.title ?? fallback;
  }
}
