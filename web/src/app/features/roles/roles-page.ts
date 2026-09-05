import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MetadataService } from '../../core/services/metadata.service';
import { RolesService, type SquadRole } from '../../core/services/roles.service';

interface Draft {
  name: string;
  label: string;
  pluralLabel: string;
  color: string;
}

/** A starting palette that avoids the seven built-in hues, so a new role reads as distinct. */
const SUGGESTED_COLORS = [
  '#F472B6',
  '#22D3EE',
  '#FB923C',
  '#4ADE80',
  '#C084FC',
  '#FACC15',
  '#94A3B8',
];

const emptyDraft = (): Draft => ({
  name: '',
  label: '',
  pluralLabel: '',
  color: SUGGESTED_COLORS[0],
});

/**
 * Roles a squad member can hold.
 *
 * The built-in seven can be renamed and recoloured — an org that says "Delivery Lead"
 * should be able to — but not removed: every board and spreadsheet written so far stores
 * their numbers. Custom roles can be retired, which takes them out of the pickers while
 * leaving people who already hold them rendering correctly.
 *
 * Saving reloads the app's reference data, so a new role appears in every dropdown
 * immediately rather than after a refresh.
 */
@Component({
  selector: 'app-roles-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './roles-page.html',
  styleUrl: './roles-page.scss',
})
export class RolesPage {
  private readonly roles = inject(RolesService);
  private readonly metadata = inject(MetadataService);

  protected readonly suggestedColors = SUGGESTED_COLORS;

  protected readonly items = signal<SquadRole[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly busyValue = signal<number | null>(null);
  protected readonly includeInactive = signal(false);

  protected readonly adding = signal(false);
  protected readonly editingValue = signal<number | null>(null);
  protected readonly draft = signal<Draft>(emptyDraft());

  protected readonly customCount = computed(() => this.items().filter((r) => !r.isBuiltIn).length);

  /** Mirrors the server rule, so the form does not offer what the API will refuse. */
  protected readonly nameProblem = computed(() => {
    if (this.editingValue() !== null) return null;

    const name = this.draft().name.trim();
    if (!name) return null;

    return /^[A-Za-z][A-Za-z0-9]*$/.test(name)
      ? null
      : 'Start with a letter, then letters and digits only — for example ScrumMaster.';
  });

  protected readonly colorProblem = computed(() =>
    /^#[0-9A-Fa-f]{6}$/.test(this.draft().color.trim())
      ? null
      : 'Use a six-digit hex colour, for example #F472B6.',
  );

  protected readonly canSave = computed(() => {
    const d = this.draft();
    if (!d.label.trim() || this.colorProblem()) return false;

    // Editing keeps the identifier, so only a new role needs one.
    if (this.editingValue() !== null) return true;

    return d.name.trim().length > 0 && !this.nameProblem();
  });

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.items.set(await this.roles.list(this.includeInactive()));
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not load the roles.'));
    } finally {
      this.loading.set(false);
    }
  }

  protected toggleInactive(value: boolean): void {
    this.includeInactive.set(value);
    void this.reload();
  }

  protected startAdd(): void {
    // Offer a colour that is not already taken, so two roles do not look alike by default.
    const used = new Set(this.items().map((r) => r.color.toUpperCase()));
    const free = SUGGESTED_COLORS.find((c) => !used.has(c.toUpperCase())) ?? SUGGESTED_COLORS[0];

    this.draft.set({ ...emptyDraft(), color: free });
    this.editingValue.set(null);
    this.adding.set(true);
    this.notice.set(null);
  }

  protected startEdit(role: SquadRole): void {
    this.draft.set({
      name: role.name,
      label: role.label,
      pluralLabel: role.pluralLabel,
      color: role.color,
    });
    this.editingValue.set(role.value);
    this.adding.set(false);
    this.notice.set(null);
  }

  protected cancel(): void {
    this.adding.set(false);
    this.editingValue.set(null);
    this.draft.set(emptyDraft());
  }

  protected updateDraft<K extends keyof Draft>(key: K, value: Draft[K]): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  /** Typing a display name suggests an identifier, which most people never need to edit. */
  protected onLabelInput(value: string): void {
    this.draft.update((d) => {
      const suggested = value.replace(/[^A-Za-z0-9]/g, '');
      const shouldSuggest =
        this.editingValue() === null &&
        (d.name === '' || d.name === d.label.replace(/[^A-Za-z0-9]/g, ''));

      return { ...d, label: value, name: shouldSuggest ? suggested : d.name };
    });
  }

  protected async save(): Promise<void> {
    if (!this.canSave()) return;

    const d = this.draft();
    this.error.set(null);

    try {
      const editing = this.editingValue();

      if (editing !== null) {
        const existing = this.items().find((r) => r.value === editing);
        await this.roles.update(editing, {
          label: d.label.trim(),
          pluralLabel: d.pluralLabel.trim() || null,
          color: d.color.trim(),
          orderIndex: existing?.orderIndex ?? editing,
        });
        this.notice.set(`${d.label.trim()} updated.`);
      } else {
        await this.roles.create({
          name: d.name.trim(),
          label: d.label.trim(),
          pluralLabel: d.pluralLabel.trim() || null,
          color: d.color.trim(),
        });
        this.notice.set(`${d.label.trim()} is now available in every role picker.`);
      }

      this.cancel();
      await this.reload();
      // Refresh the app's reference data so the new role appears without a reload.
      await this.metadata.load();
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not save the role.'));
    }
  }

  protected async setActive(role: SquadRole, isActive: boolean): Promise<void> {
    this.busyValue.set(role.value);
    this.error.set(null);

    try {
      await this.roles.setActive(role.value, isActive);
      this.notice.set(
        isActive
          ? `${role.label} is back in the role pickers.`
          : `${role.label} retired. People who already hold it keep it.`,
      );
      await this.reload();
      await this.metadata.load();
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not change the role.'));
    } finally {
      this.busyValue.set(null);
    }
  }

  private messageFrom(err: unknown, fallback: string): string {
    const body = (err as { error?: { detail?: string; title?: string } })?.error;
    return body?.detail ?? body?.title ?? fallback;
  }
}
