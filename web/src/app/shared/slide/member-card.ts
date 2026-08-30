import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { SquadMember } from '../../core/models/board.models';
import { withAlpha } from './color';

/**
 * One person on the slide, transcribed from the prototype: a 36px rounded-square
 * avatar filled solid in the role colour with white initials and a soft halo, the
 * name, the role label in that same colour, and an optional detail line.
 */
@Component({
  selector: 'app-member-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="member" [style.--member-color]="member().roleColor">
      <span class="member__ava" [style.box-shadow]="halo()" aria-hidden="true">{{
        member().initials
      }}</span>

      <div class="member__meta">
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
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 11px 12px;
      display: flex;
      align-items: center;
      gap: 10px;
      min-width: 0;
    }

    .member__ava {
      width: 36px;
      height: 36px;
      border-radius: 9px;
      flex: 0 0 auto;
      display: grid;
      place-items: center;
      font-family: var(--font-display);
      font-weight: 600;
      font-size: 13px;
      color: #fff;
      background: var(--member-color);
      /* Halo is bound inline as rgba so the PNG exporter can parse it. */
    }

    .member__meta {
      min-width: 0;
      flex: 1;
    }

    .member__name,
    .member__role,
    .member__detail {
      margin: 0;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .member__name {
      font-size: 13px;
      font-weight: 600;
    }

    .member__role {
      font-size: 11px;
      font-weight: 550;
      color: var(--member-color);
    }

    .member__allocation {
      color: var(--tx-dim);
      font-weight: 400;
    }

    .member__detail {
      font-size: 10.5px;
      color: var(--tx-mut);
      margin-top: 1px;
    }
  `,
})
export class MemberCard {
  readonly member = input.required<SquadMember>();

  /**
   * Soft halo in the role colour, as in the prototype. Bound inline as rgba rather
   * than written as `color-mix()` so the PNG exporter can parse it — see color.ts.
   */
  protected readonly halo = computed(() => `0 0 0 2px ${withAlpha(this.member().roleColor, 0.25)}`);
}
