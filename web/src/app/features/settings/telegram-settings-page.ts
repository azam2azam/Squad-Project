import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  TelegramService,
  type BoardCode,
  type TelegramConnectionResult,
  type TelegramEnrolment,
  type TelegramLink,
  type TelegramMessage,
  type TelegramSettingsView,
  type TelegramSimulation,
} from '../../core/services/telegram.service';
import { UsersService, type AppUser } from '../../core/services/users.service';

/**
 * Admin screen for the Telegram bot: the connection, who is allowed to use it, what it has
 * been sent, and a way to try a message without a phone.
 *
 * Longer than the Jira and Smartsheet screens because this integration has something they
 * do not: people. Those two authenticate as the organisation and read; this one accepts
 * writes from individuals, so half of this page is about which individuals — enrolling
 * them, listing them, and cutting one off.
 *
 * The simulator matters more than it looks. Without it the only way to find out whether a
 * template works is to create a bot, link an account and type into a phone; with it, an
 * admin can answer "why didn't my update land?" in one click, using the real pipeline.
 */
@Component({
  selector: 'app-telegram-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule, RouterLink],
  templateUrl: './telegram-settings-page.html',
  styleUrl: './telegram-settings-page.scss',
})
export class TelegramSettingsPage {
  private readonly telegram = inject(TelegramService);
  private readonly users = inject(UsersService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly testing = signal(false);
  protected readonly polling = signal(false);
  protected readonly simulating = signal(false);
  protected readonly enrolling = signal(false);
  protected readonly coding = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);

  protected readonly current = signal<TelegramSettingsView | null>(null);
  protected readonly testResult = signal<TelegramConnectionResult | null>(null);
  protected readonly pollResult = signal<string | null>(null);
  protected readonly links = signal<TelegramLink[]>([]);
  protected readonly messages = signal<TelegramMessage[]>([]);
  protected readonly people = signal<AppUser[]>([]);
  protected readonly enrolment = signal<TelegramEnrolment | null>(null);
  protected readonly simulation = signal<TelegramSimulation | null>(null);
  protected readonly boardCodes = signal<BoardCode[] | null>(null);

  // Form state.
  protected readonly baseUrl = signal('https://api.telegram.org');
  protected readonly botToken = signal('');
  protected readonly enabled = signal(false);
  protected readonly allowedChatIds = signal('');
  protected readonly replyToUnknownSenders = signal(true);

  protected readonly enrolFor = signal('');
  protected readonly sampleText = signal('#update DIS\nstatus: At Risk\nprogress: 65');
  protected readonly dryRun = signal(true);

  /** The template, printed here so nobody has to guess what the bot accepts. */
  protected readonly template = signal('');

  /** Environment configuration pins the credentials; the form is read-only in that case. */
  protected readonly locked = computed(() => this.current()?.overriddenByConfiguration ?? false);

  protected readonly hasStoredToken = computed(() => !!this.current()?.tokenHint);

  protected readonly botHandle = computed(() => {
    const username = this.testResult()?.username ?? this.current()?.botUsername;
    return username ? `@${username}` : null;
  });

  protected readonly newlyAssigned = computed(
    () => this.boardCodes()?.filter((code) => code.assigned) ?? [],
  );

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
      return 'Enter a full address, for example https://api.telegram.org';
    }

    if (url.protocol === 'https:') return null;

    if (url.protocol === 'http:') {
      const loopback =
        url.hostname === 'localhost' || url.hostname === '127.0.0.1' || url.hostname === '[::1]';
      return loopback
        ? null
        : 'Use https. The bot token is part of every request path, so http would expose it.';
    }

    return 'Enter an http(s) address.';
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      const [settings, links, messages, template] = await Promise.all([
        this.telegram.get(),
        this.telegram.links(),
        this.telegram.messages(25),
        this.telegram.template(),
      ]);

      this.apply(settings);
      this.links.set(links);
      this.messages.set(messages);
      this.template.set(template.template);
      this.error.set(null);
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not load the Telegram settings.'));
    } finally {
      this.loading.set(false);
    }

    // The people list only drives the "enrol somebody else" picker, so a failure to load
    // it must not take the rest of the screen down with it.
    try {
      const page = await this.users.list(false);
      this.people.set(page);
    } catch {
      this.people.set([]);
    }
  }

  private apply(settings: TelegramSettingsView): void {
    this.current.set(settings);
    this.baseUrl.set(settings.baseUrl);
    this.enabled.set(settings.enabled);
    this.allowedChatIds.set(settings.allowedChatIds ?? '');
    this.replyToUnknownSenders.set(settings.replyToUnknownSenders);
    this.botToken.set('');
  }

  protected async save(): Promise<void> {
    if (this.saving() || this.urlProblem()) return;

    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);

    try {
      this.apply(
        await this.telegram.save({
          baseUrl: this.baseUrl().trim(),
          // Blank keeps the stored token: the form was never given it to send back.
          botToken: this.botToken().trim() || null,
          enabled: this.enabled(),
          allowedChatIds: this.allowedChatIds().trim() || null,
          replyToUnknownSenders: this.replyToUnknownSenders(),
        }),
      );

      this.saved.set(true);
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not save the connection.'));
    } finally {
      this.saving.set(false);
    }
  }

  protected async test(): Promise<void> {
    if (this.testing()) return;

    this.testing.set(true);
    this.testResult.set(null);

    try {
      this.testResult.set(await this.telegram.test());
      this.current.set(await this.telegram.get());
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not reach Telegram.'));
    } finally {
      this.testing.set(false);
    }
  }

  protected async pollNow(): Promise<void> {
    if (this.polling()) return;

    this.polling.set(true);
    this.pollResult.set(null);

    try {
      const report = await this.telegram.pollNow();
      this.pollResult.set(report.message);
      this.messages.set(await this.telegram.messages(25));
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not read messages.'));
    } finally {
      this.polling.set(false);
    }
  }

  protected async disconnect(): Promise<void> {
    if (this.saving()) return;

    this.saving.set(true);

    try {
      await this.telegram.clear();
      this.apply(await this.telegram.get());
      this.testResult.set(null);
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not remove the connection.'));
    } finally {
      this.saving.set(false);
    }
  }

  protected async createEnrolment(): Promise<void> {
    if (this.enrolling()) return;

    this.enrolling.set(true);
    this.enrolment.set(null);

    try {
      this.enrolment.set(await this.telegram.createEnrolment(this.enrolFor() || null));
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not create an enrolment code.'));
    } finally {
      this.enrolling.set(false);
    }
  }

  protected async revoke(link: TelegramLink): Promise<void> {
    try {
      await this.telegram.revokeLink(link.id);
      this.links.set(await this.telegram.links());
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not revoke that link.'));
    }
  }

  protected async simulate(): Promise<void> {
    if (this.simulating()) return;

    this.simulating.set(true);
    this.simulation.set(null);

    try {
      this.simulation.set(await this.telegram.simulate(this.sampleText(), this.dryRun()));

      // A real run changes boards, so the log and the message list are refreshed with it.
      if (!this.dryRun()) this.messages.set(await this.telegram.messages(25));
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not run that message.'));
    } finally {
      this.simulating.set(false);
    }
  }

  protected async assignBoardCodes(): Promise<void> {
    if (this.coding()) return;

    this.coding.set(true);

    try {
      this.boardCodes.set(await this.telegram.assignBoardCodes());
    } catch (err) {
      this.error.set(this.messageFrom(err, 'Could not assign board codes.'));
    } finally {
      this.coding.set(false);
    }
  }

  private messageFrom(err: unknown, fallback: string): string {
    const problem = (err as { error?: { detail?: string; title?: string } })?.error;
    return problem?.detail ?? problem?.title ?? fallback;
  }
}
