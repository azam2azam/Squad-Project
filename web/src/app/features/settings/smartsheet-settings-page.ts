import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  SmartsheetService,
  type SmartsheetConnectionResult,
  type SmartsheetSettingsView,
  type SmartsheetSyncReport,
} from '../../core/services/smartsheet.service';

/**
 * Admin screen for connecting the board to the company's Smartsheet.
 *
 * The Smartsheet twin of the Jira settings screen, and deliberately the same shape, with
 * two differences that come from the provider rather than from taste:
 *
 * 1. Smartsheet authenticates with a single bearer access token — there is no account
 *    email to collect.
 * 2. A sheet is a spreadsheet, so it has no universal notion of "done". The column
 *    mapping below is how the sync is told which cells carry progress and status; without
 *    it there would be nothing to read.
 */
@Component({
  selector: 'app-smartsheet-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  templateUrl: './smartsheet-settings-page.html',
  styleUrl: './jira-settings-page.scss',
})
export class SmartsheetSettingsPage {
  private readonly smartsheet = inject(SmartsheetService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly testing = signal(false);
  protected readonly syncing = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);

  protected readonly current = signal<SmartsheetSettingsView | null>(null);
  protected readonly testResult = signal<SmartsheetConnectionResult | null>(null);
  protected readonly syncResult = signal<SmartsheetSyncReport | null>(null);

  // Form state.
  protected readonly baseUrl = signal('https://api.smartsheet.com/2.0');
  protected readonly accessToken = signal('');
  protected readonly enabled = signal(false);
  protected readonly autoApply = signal(false);
  protected readonly syncIntervalMinutes = signal(30);
  protected readonly progressColumn = signal('% Complete');
  protected readonly statusColumn = signal('Status');
  protected readonly probeSheetId = signal('');

  /** Environment configuration pins the credentials; the form is read-only in that case. */
  protected readonly locked = computed(() => this.current()?.overriddenByConfiguration ?? false);

  protected readonly hasStoredToken = computed(() => !!this.current()?.tokenHint);

  /**
   * Mirrors the server's rule exactly, so the form never refuses something the API would
   * accept: https anywhere, http only for loopback.
   */
  protected readonly urlProblem = computed(() => {
    const value = this.baseUrl().trim();
    if (!value) return null;

    let url: URL;
    try {
      url = new URL(value);
    } catch {
      return 'Enter a full address, for example https://api.smartsheet.com/2.0';
    }

    if (url.protocol === 'https:') return null;

    if (url.protocol === 'http:') {
      const loopback =
        url.hostname === 'localhost' || url.hostname === '127.0.0.1' || url.hostname === '[::1]';
      return loopback
        ? null
        : 'Use an https:// address. An access token sent over http can be read in transit.';
    }

    return 'Enter a full address, for example https://api.smartsheet.com/2.0';
  });

  protected readonly canSave = computed(
    () =>
      !this.locked() &&
      !this.saving() &&
      this.baseUrl().trim().length > 0 &&
      this.urlProblem() === null &&
      // A first-time save needs a token; later saves may reuse the stored one.
      (this.hasStoredToken() || this.accessToken().trim().length > 0),
  );

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.applyToForm(await this.smartsheet.get());
    } catch {
      this.error.set('Could not load the Smartsheet settings.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (!this.canSave()) return;

    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);

    try {
      const view = await this.smartsheet.save({
        baseUrl: this.baseUrl().trim(),
        // Blank is meaningful: it tells the server to keep the token it already holds.
        accessToken: this.accessToken().trim() || null,
        enabled: this.enabled(),
        autoApply: this.autoApply(),
        syncIntervalMinutes: this.syncIntervalMinutes(),
        progressColumn: this.progressColumn().trim() || null,
        statusColumn: this.statusColumn().trim() || null,
      });

      this.applyToForm(view);
      this.saved.set(true);
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not save the Smartsheet settings.'));
    } finally {
      this.saving.set(false);
    }
  }

  protected async test(): Promise<void> {
    this.testing.set(true);
    this.testResult.set(null);
    this.error.set(null);

    try {
      this.testResult.set(await this.smartsheet.test(this.probeSheetId().trim() || null));
    } catch (err) {
      this.error.set(this.messageFrom(err, 'The connection test could not be run.'));
    } finally {
      this.testing.set(false);
    }
  }

  protected async syncNow(): Promise<void> {
    this.syncing.set(true);
    this.syncResult.set(null);
    this.error.set(null);

    try {
      this.syncResult.set(await this.smartsheet.syncNow());
      // The run records its own timestamp, so refresh to show it.
      this.applyToForm(await this.smartsheet.get());
    } catch (err) {
      this.error.set(this.messageFrom(err, 'The sync could not be run.'));
    } finally {
      this.syncing.set(false);
    }
  }

  protected async disconnect(): Promise<void> {
    if (!confirm('Remove the stored Smartsheet connection, including the access token?')) return;

    this.saving.set(true);
    this.error.set(null);

    try {
      await this.smartsheet.clear();
      this.accessToken.set('');
      this.testResult.set(null);
      this.syncResult.set(null);
      await this.reload();
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not remove the connection.'));
    } finally {
      this.saving.set(false);
    }
  }

  protected formatDate(value: string | null): string {
    return value ? new Date(value).toLocaleString() : 'never';
  }

  private applyToForm(view: SmartsheetSettingsView): void {
    this.current.set(view);
    this.baseUrl.set(view.baseUrl);
    this.enabled.set(view.enabled);
    this.autoApply.set(view.autoApply);
    this.syncIntervalMinutes.set(view.syncIntervalMinutes || 30);
    this.progressColumn.set(view.progressColumn);
    this.statusColumn.set(view.statusColumn);
    // Never repopulate the token field: the server does not send it, and a masked
    // placeholder in an input invites someone to "save" the mask as the new token.
    this.accessToken.set('');
  }

  /** Surfaces the server's ProblemDetails message rather than a generic failure. */
  private messageFrom(err: unknown, fallback: string): string {
    const detail = (err as { error?: { detail?: string; title?: string } })?.error;
    return detail?.detail ?? detail?.title ?? fallback;
  }
}
