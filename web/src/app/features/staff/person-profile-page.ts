import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import {
  StaffService,
  type PersonProfile,
  type StaffOption,
} from '../../core/services/staff.service';

interface AvailabilityDraft {
  id: string | null;
  fromDate: string;
  toDate: string;
  kind: number;
  capacityPercent: number;
  note: string;
}

const today = () => new Date().toISOString().slice(0, 10);

const emptyAvailability = (): AvailabilityDraft => ({
  id: null,
  fromDate: today(),
  toDate: today(),
  kind: 0,
  capacityPercent: 0,
  note: '',
});

/**
 * One person, end to end: what they are committed to, when they are away, the work on
 * them, and what they have actually changed.
 *
 * The activity feed is matched on the display name the audit trail recorded, so the page
 * prints how the match was made rather than presenting a possibly-partial history as if
 * it were complete.
 */
@Component({
  selector: 'app-person-profile-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './person-profile-page.html',
  styleUrl: './person-profile-page.scss',
})
export class PersonProfilePage {
  private readonly route = inject(ActivatedRoute);
  private readonly staff = inject(StaffService);
  private readonly auth = inject(AuthService);

  protected readonly isAdmin = this.auth.isAdmin;

  protected readonly profile = signal<PersonProfile | null>(null);
  protected readonly kinds = signal<StaffOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly showAvailabilityForm = signal(false);
  protected readonly draft = signal<AvailabilityDraft>(emptyAvailability());

  private readonly personId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('id'))),
    { initialValue: null },
  );

  protected readonly openWork = computed(
    () => this.profile()?.workItems.filter((w) => w.status !== 3) ?? [],
  );

  protected readonly doneWork = computed(
    () => this.profile()?.workItems.filter((w) => w.status === 3) ?? [],
  );

  protected readonly canSaveDraft = computed(() => {
    const d = this.draft();
    return !!d.fromDate && !!d.toDate && d.toDate >= d.fromDate;
  });

  constructor() {
    void this.reload();
    void this.staff
      .options()
      .then((o) => this.kinds.set(o.availabilityKinds))
      .catch(() => this.kinds.set([]));
  }

  protected async reload(): Promise<void> {
    const id = this.personId();
    if (!id) return;

    this.loading.set(true);
    this.error.set(null);

    try {
      this.profile.set(await this.staff.profile(id));
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not load this person.'));
    } finally {
      this.loading.set(false);
    }
  }

  protected startAvailability(): void {
    this.draft.set(emptyAvailability());
    this.showAvailabilityForm.set(true);
    this.notice.set(null);
  }

  protected editAvailability(id: string): void {
    const record = this.profile()?.availability.find((a) => a.id === id);
    if (!record) return;

    this.draft.set({
      id: record.id,
      fromDate: record.fromDate,
      toDate: record.toDate,
      kind: record.kind,
      capacityPercent: record.capacityPercent,
      note: record.note ?? '',
    });
    this.showAvailabilityForm.set(true);
    this.notice.set(null);
  }

  protected cancelAvailability(): void {
    this.showAvailabilityForm.set(false);
    this.draft.set(emptyAvailability());
  }

  protected updateDraft<K extends keyof AvailabilityDraft>(
    key: K,
    value: AvailabilityDraft[K],
  ): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  protected async saveAvailability(): Promise<void> {
    const id = this.personId();
    const d = this.draft();
    if (!id || !this.canSaveDraft()) return;

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.staff.saveAvailability({
        id: d.id,
        personId: id,
        fromDate: d.fromDate,
        toDate: d.toDate,
        kind: d.kind,
        capacityPercent: d.capacityPercent,
        note: d.note.trim() || null,
      });

      this.cancelAvailability();
      this.notice.set('Availability saved. Capacity recalculated.');
      await this.reload();
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not save that availability.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeAvailability(id: string): Promise<void> {
    if (!confirm('Remove this availability record?')) return;

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.staff.deleteAvailability(id);
      this.notice.set('Availability removed.');
      await this.reload();
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not remove that record.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected formatDate(value: string | null): string {
    if (!value) return '—';
    return new Date(value).toLocaleDateString(undefined, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  }

  protected formatRange(from: string, to: string): string {
    return from === to ? this.formatDate(from) : `${this.formatDate(from)} → ${this.formatDate(to)}`;
  }

  protected formatWhen(value: string): string {
    return new Date(value).toLocaleString();
  }

  /** An assignment with no dates runs until further notice, which is worth saying. */
  protected assignmentPeriod(startsOn: string | null, endsOn: string | null): string {
    if (!startsOn && !endsOn) return 'Ongoing';
    if (startsOn && !endsOn) return `From ${this.formatDate(startsOn)}`;
    if (!startsOn && endsOn) return `Until ${this.formatDate(endsOn)}`;
    return `${this.formatDate(startsOn)} → ${this.formatDate(endsOn)}`;
  }

  private messageFrom(err: unknown, fallback: string): string {
    const body = (err as { error?: { detail?: string; title?: string } })?.error;
    return body?.detail ?? body?.title ?? fallback;
  }
}
