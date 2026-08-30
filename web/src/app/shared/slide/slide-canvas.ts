import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { BoardDetail } from '../../core/models/board.models';
import { CompositionBar } from './composition-bar';
import { MemberCard } from './member-card';
import { ProgressRing } from './progress-ring';
import { StatusBadge } from './status-badge';

/**
 * The deliverable itself: the dark slide.
 *
 * Transcribed from the prototype (squad-status-board.html) which is the acceptance
 * baseline for visual fidelity — the radial wash, the ring-left/composition-right
 * grid, the eyebrow product chip, the "The squad" rule, and the auto-fill member
 * grid all come from it.
 *
 * Deliberately free of routing, services and stores: it renders whatever board it is
 * handed, which is what lets Present mode and the headless export renderer mount it
 * in isolation (spec section 9). It reflows to its container, as the prototype does,
 * rather than being scaled.
 */
@Component({
  selector: 'app-slide-canvas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProgressRing, CompositionBar, MemberCard, StatusBadge],
  template: `
    <section class="slide">
      <div class="slide__inner">
        <header class="slide__head">
          <div class="slide__headings">
            <p class="slide__eyebrow">
              <span class="slide__tag">{{ board().product }}</span>
              @if (board().sprint) {
                <span class="slide__sprint">{{ board().sprint }}</span>
              }
            </p>

            <h1 class="slide__title">{{ board().title }}</h1>

            <p class="slide__squad">
              Delivered by <b>{{ board().squadName }}</b>
            </p>
          </div>

          <app-status-badge [label]="board().statusLabel" [color]="board().statusColor" />
        </header>

        <div class="slide__grid">
          <app-progress-ring [percent]="board().progressPercent" />

          <app-composition-bar
            [composition]="board().composition"
            [headingCount]="memberCountLabel()"
          />
        </div>

        <p class="slide__team-h">The squad</p>

        <div class="slide__team">
          @for (member of board().members; track member.id) {
            <app-member-card [member]="member" />
          } @empty {
            <p class="slide__team-empty">Add squad members to populate the board.</p>
          }
        </div>

        <footer class="slide__foot">
          <span>
            <span class="slide__foot-k">Board:</span> Product Innovation &amp; Revamp Team
          </span>
          @if (board().blockerNote) {
            <span class="slide__blocker">Blocker: {{ board().blockerNote }}</span>
          }
          <span>{{ updatedLabel() }}</span>
        </footer>
      </div>
    </section>
  `,
  styles: `
    :host {
      display: block;
    }

    .slide {
      position: relative;
      background: var(--ink);
      border-radius: 18px;
      overflow: hidden;
      color: var(--tx);
      box-shadow: 0 24px 60px -24px rgba(14, 21, 32, 0.55);
    }

    /* Two soft washes — teal from the top-right, indigo from the bottom-left. */
    .slide::before {
      content: '';
      position: absolute;
      inset: 0;
      pointer-events: none;
      background:
        radial-gradient(120% 90% at 100% 0%, rgba(45, 212, 191, 0.12), transparent 55%),
        radial-gradient(90% 80% at 0% 100%, rgba(99, 102, 241, 0.12), transparent 55%);
    }

    .slide__inner {
      position: relative;
      padding: 30px 32px 32px;
    }

    .slide__head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 16px;
      margin-bottom: 22px;
    }

    .slide__headings {
      min-width: 0;
    }

    .slide__eyebrow {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0 0 9px;
      font-size: 11.5px;
      font-weight: 550;
      letter-spacing: 0.09em;
      text-transform: uppercase;
      color: var(--tx-mut);
    }

    .slide__tag {
      background: var(--panel-2);
      border: 1px solid var(--line);
      padding: 3px 9px;
      border-radius: 6px;
      color: var(--tx);
    }

    .slide__title {
      margin: 0;
      font-family: var(--font-display);
      font-size: 29px;
      font-weight: 600;
      line-height: 1.08;
      letter-spacing: -0.02em;
    }

    .slide__squad {
      margin: 8px 0 0;
      font-size: 13.5px;
      color: var(--tx-mut);

      b {
        color: var(--tx);
        font-weight: 600;
      }
    }

    /* Ring left at a fixed 150px, composition taking the rest. */
    .slide__grid {
      display: grid;
      grid-template-columns: 150px 1fr;
      gap: 26px;
      align-items: center;
      margin-bottom: 24px;
    }

    @media (max-width: 520px) {
      .slide__grid {
        grid-template-columns: 1fr;
        gap: 20px;
        justify-items: center;
        text-align: center;
      }
    }

    .slide__team-h {
      display: flex;
      align-items: center;
      gap: 9px;
      margin: 0 0 12px;
      font-size: 11.5px;
      font-weight: 550;
      letter-spacing: 0.09em;
      text-transform: uppercase;
      color: var(--tx-mut);

      /* Trailing rule that fills the remaining width. */
      &::after {
        content: '';
        flex: 1;
        height: 1px;
        background: var(--line);
      }
    }

    .slide__team {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
      gap: 10px;
    }

    .slide__team-empty {
      grid-column: 1 / -1;
      margin: 0;
      text-align: center;
      padding: 18px;
      color: var(--tx-mut);
      font-size: 13px;
      border: 1px dashed var(--line);
      border-radius: 12px;
    }

    .slide__foot {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 16px;
      margin-top: 24px;
      padding-top: 16px;
      border-top: 1px solid var(--line);
      font-size: 11.5px;
      color: var(--tx-dim);
    }

    .slide__foot-k {
      color: var(--tx-mut);
    }

    .slide__blocker {
      color: var(--status-blocked);
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
})
export class SlideCanvas {
  readonly board = input.required<BoardDetail>();

  protected readonly memberCountLabel = computed(() => {
    const count = this.board().members.length;
    return count === 1 ? '1 person' : `${count} people`;
  });

  protected readonly updatedLabel = computed(() => {
    const raw = this.board().updatedAt;
    if (!raw) return '';

    const date = new Date(raw);
    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString('en-GB', {
          day: 'numeric',
          month: 'short',
          year: 'numeric',
        });
  });
}
