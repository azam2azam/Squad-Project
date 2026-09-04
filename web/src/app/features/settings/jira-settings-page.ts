import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  IntegrationsService,
  type JiraConnectionResult,
  type JiraSettingsView,
  type JiraSyncReport,
} from '../../core/services/integrations.service';

/**
 * Admin screen for connecting the board to the company's Jira.
 *
 * Two deliberate choices shape this screen:
 *
 * 1. The API token is write-only. It is never sent to the browser, so the field starts
 *    empty even when a token is stored, and leaving it empty means "keep the stored one".
 *    The mask under the field is how an admin confirms *which* token is in place.
 * 2. Auto-apply is off by default. With it off, Jira only *suggests* figures in the board
 *    editor and a Product Owner accepts them. Turning it on lets a background job write to
 *    boards unattended, which is a real decision, so the screen states what it means.
 */
@Component({
  selector: 'app-jira-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './jira-settings-page.html',
  styleUrl: './jira-settings-page.scss',
})
export class JiraSettingsPage {
  private readonly integrations = inject(IntegrationsService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly testing = signal(false);
  protected readonly syncing = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);

  protected readonly current = signal<JiraSettingsView | null>(null);
  protected readonly testResult = signal<JiraConnectionResult | null>(null);
  protected readonly syncResult = signal<JiraSyncReport | null>(null);

  // Form state.
  protected readonly baseUrl = signal('');
  protected readonly email = signal('');
  protected readonly apiToken = signal('');
  protected readonly enabled = signal(false);
  protected readonly autoApply = signal(false);
  protected readonly syncIntervalMinutes = signal(30);
  protected readonly probeProjectKey = signal('');

  /** Environment configuration pins the credentials; the form is read-only in that case. */
  protected readonly locked = computed(() => this.current()?.overriddenByConfiguration ?? false);

  protected readonly hasStoredToken = computed(() => !!this.current()?.tokenHint);

  /**
   * Mirrors the server's rule exactly, so the form never refuses something the API would
   * accept: https anywhere, http only for loopback (a Jira running on the same machine,
   * where nothing leaves the box). Anything else would put the token on the wire in clear.
   */
  protected readonly urlProblem = computed(() => {
    const value = this.baseUrl().trim();
    if (!value) return null;

    let url: URL;
    try {
      url = new URL(value);
    } catch {
      return 'Enter a full address, for example https://yourcompany.atlassian.net';
    }

    if (url.protocol === 'https:') return null;

    if (url.protocol === 'http:') {
      const loopback =
        url.hostname === 'localhost' || url.hostname === '127.0.0.1' || url.hostname === '[::1]';
      return loopback
        ? null
        : 'Use an https:// address. A token sent over http can be read in transit.';
    }

    return 'Enter a full address, for example https://yourcompany.atlassian.net';
  });

  protected readonly canSave = computed(
    () =>
      !this.locked() &&
      !this.saving() &&
      this.baseUrl().trim().length > 0 &&
      this.email().trim().length > 0 &&
      this.urlProblem() === null &&
      // A first-time save needs a token; later saves may reuse the stored one.
      (this.hasStoredToken() || this.apiToken().trim().length > 0),
  );

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const view = await this.integrations.get();
      this.applyToForm(view);
    } catch {
      this.error.set('Could not load the Jira settings.');
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
      const view = await this.integrations.save({
        baseUrl: this.baseUrl().trim(),
        email: this.email().trim(),
        // Blank is meaningful: it tells the server to keep the token it already holds.
        apiToken: this.apiToken().trim() || null,
        enabled: this.enabled(),
        autoApply: this.autoApply(),
        syncIntervalMinutes: this.syncIntervalMinutes(),
      });

      this.applyToForm(view);
      this.saved.set(true);
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not save the Jira settings.'));
    } finally {
      this.saving.set(false);
    }
  }

  protected async test(): Promise<void> {
    this.testing.set(true);
    this.testResult.set(null);
    this.error.set(null);

    try {
      this.testResult.set(await this.integrations.test(this.probeProjectKey().trim() || null));
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
      this.syncResult.set(await this.integrations.syncNow());
      // The run records its own timestamp, so refresh to show it.
      this.applyToForm(await this.integrations.get());
    } catch (err) {
      this.error.set(this.messageFrom(err, 'The sync could not be run.'));
    } finally {
      this.syncing.set(false);
    }
  }

  protected async disconnect(): Promise<void> {
    if (!confirm('Remove the stored Jira connection, including the API token?')) return;

    this.saving.set(true);
    this.error.set(null);

    try {
      await this.integrations.clear();
      this.apiToken.set('');
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
    if (!value) return 'never';
    return new Date(value).toLocaleString();
  }

  private applyToForm(view: JiraSettingsView): void {
    this.current.set(view);
    this.baseUrl.set(view.baseUrl);
    this.email.set(view.email);
    this.enabled.set(view.enabled);
    this.autoApply.set(view.autoApply);
    this.syncIntervalMinutes.set(view.syncIntervalMinutes || 30);
    // Never repopulate the token field: the server does not send it, and a masked
    // placeholder in an input invites someone to "save" the mask as the new token.
    this.apiToken.set('');
  }

  /** Surfaces the server's ProblemDetails message rather than a generic failure. */
  private messageFrom(err: unknown, fallback: string): string {
    const detail = (err as { error?: { detail?: string; title?: string } })?.error;
    return detail?.detail ?? detail?.title ?? fallback;
  }
}
