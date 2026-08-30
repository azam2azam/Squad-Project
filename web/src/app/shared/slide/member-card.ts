import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { SquadMember } from '../../core/models/board.models';

/**
 * One person on the slide: initials avatar tinted to their role colour, name,
 * role label in that same colour, and an optional detail line.
 */
@Component({
  selector: 'app-member-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="member" [style.--member-color]="member().roleColor">
      <span class="member__avatar" aria-hidden="true">{{ member().initials }}</span>

      <div class="member__body">
        <p class="member__name">{{ member().fullName }}</p>
        <p class="member__role">
          {{ member().roleLabel }}
          @if (member().allocationPercent !== null) {
            <span class="member__allocation">· {{ member().allocationPercent }}%</span>
          }
        </p>
        @if (member().detail) {
          <p class="member__detail">{{ member().detail }}</p>
        }
      </div>
    </article>
  `,
  styles: `
    .member {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 12px 14px;
      background: var(--panel-2);
      border: 1px solid var(--line);
      border-radius: var(--radius-md);
      min-width: 0;
    }

    .member__avatar {
      width: 38px;
      height: 38px;
      border-radius: 50%;
      flex: none;
      display: grid;
      place-items: center;
      font-family: var(--font-display);
      font-weight: 600;
      font-size: 14px;
      letter-spacing: 0.02em;
      color: var(--member-color);
      background: color-mix(in srgb, var(--member-color) 16%, transparent);
      border: 1px solid color-mix(in srgb, var(--member-color) 42%, transparent);
    }

    .member__body {
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 1px;
    }

    .member__name,
    .member__role,
    .member__detail {
      margin: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .member__name {
      font-weight: 600;
      font-size: 14px;
      color: var(--tx);
    }

    .member__role {
      font-size: 12px;
      font-weight: 500;
      color: var(--member-color);
    }

    .member__allocation {
      color: var(--tx-dim);
      font-weight: 400;
    }

    .member__detail {
      font-size: 11.5px;
      color: var(--tx-dim);
    }
  `,
})
export class MemberCard {
  readonly member = input.required<SquadMember>();
}
