import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { BoardDetail } from '../../core/models/board.models';
import { CompositionBar } from './composition-bar';
import { MemberCard } from './member-card';
import { ProgressRing } from './progress-ring';
import { StatusBadge } from './status-badge';

/**
 * The deliverable itself: the dark 16:9 slide.
 *
 * Deliberately self-contained and free of routing, services and stores — it renders
 * whatever board it is handed. That is what lets Present mode and the headless export
 * renderer mount it in isolation (spec section 9).
 *
 * It renders at a fixed 1280x720 and is scaled by its host via a CSS transform, so the
 * on-screen preview and a 2x export are the same geometry rather than two layouts that
 * drift apart.
 */
@Component({
  selector: 'app-slide-canvas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProgressRing, CompositionBar, MemberCard, StatusBadge],
  template: `
    <section class="slide" [class.slide--dense]="isDense()">
      <header class="slide__head">
        <div class="slide__headings">
          <p class="slide__eyebrow">
            <span class="slide__product">{{ board().product }}</span>
            @if (board().sprint) {
              <span class="slide__sep" aria-hidden="true">·</span>
              <span class="slide__sprint">{{ board().sprint }}</span>
            }
          </p>

          <h1 class="slide__title">{{ board().title }}</h1>

          <p class="slide__squad">
            <span class="slide__squad-name">{{ board().squadName }}</span>
            <span class="slide__sep" aria-hidden="true">·</span>
            <span class="slide__squad-size">{{ memberCountLabel() }}</span>
          </p>
        </div>

        <div class="slide__meta">
          <app-status-badge [label]="board().statusLabel" [color]="board().statusColor" />
          <app-progress-ring
            [percent]="board().progressPercent"
            [color]="board().statusColor"
            [size]="132"
          />
        </div>
      </header>

      <div class="slide__composition">
        <app-composition-bar [composition]="board().composition" />
      </div>

      <div class="slide__members" [style.--member-columns]="memberColumns()">
        @for (member of board().members; track member.id) {
          <app-member-card [member]="member" />
        } @empty {
          <p class="slide__empty">No squad members yet — add people to build the squad.</p>
        }
      </div>

      <footer class="slide__foot">
        <span class="slide__foot-brand">Squad Status Board</span>
        <span class="slide__sep" aria-hidden="true">·</span>
        <span>PIRT</span>
        @if (board().blockerNote) {
          <span class="slide__blocker">
            <span class="slide__sep" aria-hidden="true">·</span>
            Blocker: {{ board().blockerNote }}
          </span>
        }
        <span class="slide__foot-spacer"></span>
        <span class="slide__foot-updated">Updated {{ updatedLabel() }}</span>
      </footer>
    </section>
  `,
  styles: `
    :host {
      display: block;
      /* Fixed geometry: the export renders this exact box at 2x. */
      width: 1280px;
      height: 720px;
    }

    .slide {
      width: 100%;
      height: 100%;
      box-sizing: border-box;
      padding: 52px 56px 32px;
      background: var(--ink);
      color: var(--tx);
      font-family: var(--font-ui);
      display: flex;
      flex-direction: column;
      gap: 26px;
      overflow: hidden;
    }

    .slide__head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 40px;
    }

    .slide__headings {
      min-width: 0;
    }

    .slide__eyebrow {
      margin: 0 0 12px;
      font-size: 13px;
      font-weight: 600;
      letter-spacing: 0.16em;
      text-transform: uppercase;
      color: var(--accent);
    }

    .slide__sprint {
      color: var(--tx-mut);
    }

    .slide__sep {
      margin: 0 8px;
      color: var(--tx-dim);
    }

    .slide__title {
      margin: 0;
      font-family: var(--font-display);
      font-size: 52px;
      font-weight: 600;
      line-height: 1.05;
      letter-spacing: -0.025em;
      color: var(--tx);
    }

    .slide__squad {
      margin: 14px 0 0;
      font-size: 16px;
      color: var(--tx-mut);
    }

    .slide__squad-name {
      color: var(--tx);
      font-weight: 600;
    }

    .slide__meta {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 18px;
      flex: none;
    }

    .slide__composition {
      padding: 18px 20px;
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: var(--radius-lg);
    }

    .slide__members {
      display: grid;
      grid-template-columns: repeat(var(--member-columns, 3), minmax(0, 1fr));
      gap: 12px;
      align-content: start;
      flex: 1 1 auto;
      min-height: 0;
    }

    .slide__empty {
      grid-column: 1 / -1;
      margin: 0;
      padding: 24px;
      text-align: center;
      color: var(--tx-dim);
      background: var(--panel);
      border: 1px dashed var(--line);
      border-radius: var(--radius-md);
    }

    .slide__foot {
      display: flex;
      align-items: center;
      gap: 0;
      padding-top: 14px;
      border-top: 1px solid var(--line);
      font-size: 12px;
      color: var(--tx-dim);
      flex: none;
    }

    .slide__foot-brand {
      color: var(--tx-mut);
      font-weight: 600;
    }

    .slide__blocker {
      color: var(--status-blocked);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .slide__foot-spacer {
      flex: 1 1 auto;
    }

    /* Large squads: tighten the title so the member grid keeps its room. */
    .slide--dense .slide__title {
      font-size: 44px;
    }

    .slide--dense .slide__members {
      gap: 10px;
    }
  `,
})
export class SlideCanvas {
  readonly board = input.required<BoardDetail>();

  /** Beyond nine people the grid needs a fourth column to stay on one screen. */
  protected readonly memberColumns = computed(() => (this.board().members.length > 9 ? 4 : 3));

  protected readonly isDense = computed(() => this.board().members.length > 9);

  protected readonly memberCountLabel = computed(() => {
    const count = this.board().members.length;
    return count === 1 ? '1 person' : `${count} people`;
  });

  protected readonly updatedLabel = computed(() => {
    const raw = this.board().updatedAt;
    if (!raw) return '—';

    const date = new Date(raw);
    return Number.isNaN(date.getTime())
      ? '—'
      : date.toLocaleDateString(undefined, {
          year: 'numeric',
          month: 'short',
          day: 'numeric',
        });
  });
}
