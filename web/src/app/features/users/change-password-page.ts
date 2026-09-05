import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MIN_PASSWORD_LENGTH, UsersService } from '../../core/services/users.service';

/**
 * Changing your own password.
 *
 * Open to every signed-in user, and the reason an admin-set password is acceptable: the
 * person can replace the one the administrator knows. Requires the current password, so a
 * borrowed session cannot lock the owner out of their own account.
 */
@Component({
  selector: 'app-change-password-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <div class="pw">
      <h1 class="pw__title">Change password</h1>
      <p class="pw__sub">
        If an administrator set your password, change it here — they know the one they gave
        you.
      </p>

      @if (error()) {
        <p class="pw__banner pw__banner--error" role="alert">{{ error() }}</p>
      }
      @if (done()) {
        <p class="pw__banner pw__banner--ok" role="status">
          Password changed. It applies the next time you sign in.
        </p>
      }

      <form class="pw__card" (submit)="$event.preventDefault(); submit()">
        <label class="pw__field">
          <span class="pw__label">Current password</span>
          <input
            class="pw__input"
            type="password"
            autocomplete="current-password"
            [value]="current()"
            (input)="current.set($any($event.target).value)"
          />
        </label>

        <label class="pw__field">
          <span class="pw__label">
            New password
            <span class="pw__hint">— at least {{ minLength }} characters</span>
          </span>
          <input
            class="pw__input"
            type="password"
            autocomplete="new-password"
            [value]="next()"
            (input)="next.set($any($event.target).value)"
          />
        </label>

        <label class="pw__field">
          <span class="pw__label">Confirm new password</span>
          <input
            class="pw__input"
            type="password"
            autocomplete="new-password"
            [value]="confirm()"
            (input)="confirm.set($any($event.target).value)"
          />
          @if (mismatch()) {
            <span class="pw__error">The two new passwords do not match.</span>
          }
        </label>

        <button type="submit" class="pw__btn" [disabled]="!canSubmit()">
          {{ saving() ? 'Saving…' : 'Change password' }}
        </button>
      </form>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .pw {
      max-width: 460px;
      margin: 0 auto;
      padding: 32px 24px 60px;
    }

    .pw__title {
      margin: 0;
      font-size: 24px;
    }

    .pw__sub {
      margin: 6px 0 18px;
      color: #4e5b6e;
      font-size: 13px;
      line-height: 1.6;
    }

    .pw__banner {
      margin: 0 0 14px;
      padding: 11px 14px;
      border-radius: var(--radius-sm);
      border: 1px solid var(--card-line);
      font-size: 13px;

      &--error {
        border-color: #fca5a5;
        background: #fef2f2;
        color: #991b1b;
      }

      &--ok {
        border-color: #6ee7b7;
        background: #ecfdf5;
        color: #065f46;
      }
    }

    .pw__card {
      display: flex;
      flex-direction: column;
      gap: 14px;
      padding: 20px;
      border: 1px solid var(--card-line);
      border-radius: var(--radius-md);
      background: var(--card);
      box-shadow: var(--shadow-card);
    }

    .pw__field {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .pw__label {
      font-size: 12px;
      font-weight: 600;
      color: #4e5b6e;
    }

    .pw__hint {
      font-weight: 400;
      color: #5d6b7e;
    }

    .pw__error {
      font-size: 12px;
      color: #991b1b;
    }

    .pw__input {
      width: 100%;
      padding: 9px 11px;
      border: 1px solid var(--card-line);
      border-radius: var(--radius-sm);
      background: #fff;
      color: var(--ink-tx);
    }

    .pw__input:focus {
      outline: none;
      border-color: var(--accent-deep);
    }

    .pw__btn {
      align-self: flex-start;
      padding: 9px 16px;
      border-radius: var(--radius-sm);
      border: 1px solid var(--ink);
      background: var(--ink);
      color: var(--tx);
      font-weight: 600;
      cursor: pointer;
    }

    .pw__btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
  `,
})
export class ChangePasswordPage {
  private readonly users = inject(UsersService);

  protected readonly minLength = MIN_PASSWORD_LENGTH;

  protected readonly current = signal('');
  protected readonly next = signal('');
  protected readonly confirm = signal('');
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly done = signal(false);

  protected readonly mismatch = computed(
    () => this.confirm().length > 0 && this.confirm() !== this.next(),
  );

  protected readonly canSubmit = computed(
    () =>
      !this.saving() &&
      this.current().length > 0 &&
      this.next().length >= this.minLength &&
      this.next() === this.confirm(),
  );

  protected async submit(): Promise<void> {
    if (!this.canSubmit()) return;

    this.saving.set(true);
    this.error.set(null);
    this.done.set(false);

    try {
      await this.users.changeOwnPassword(this.current(), this.next());
      this.current.set('');
      this.next.set('');
      this.confirm.set('');
      this.done.set(true);
    } catch (err) {
      const body = (err as { error?: { detail?: string; title?: string } })?.error;
      this.error.set(body?.detail ?? body?.title ?? 'Could not change the password.');
    } finally {
      this.saving.set(false);
    }
  }
}
