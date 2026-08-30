import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { Composition } from '../../core/models/board.models';

/**
 * Stacked bar showing the squad's role mix, plus the legend beneath it.
 *
 * Segment widths come from the server-computed percentages, which are guaranteed to sum
 * to 100 — the bar cannot end with a sliver of background from rounding. The counts are
 * also rendered as text in the legend, so the information is never colour-only.
 */
@Component({
  selector: 'app-composition-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="comp">
      <div
        class="comp__bar"
        role="img"
        [attr.aria-label]="'Squad composition: ' + composition().legendText"
      >
        @for (segment of composition().segments; track segment.role) {
          <span
            class="comp__segment"
            [style.width.%]="segment.percent"
            [style.background]="segment.color"
            [title]="segment.count + ' × ' + segment.label"
          ></span>
        } @empty {
          <span class="comp__segment comp__segment--empty"></span>
        }
      </div>

      @if (showLegend()) {
        <ul class="comp__legend">
          @for (segment of composition().segments; track segment.role) {
            <li class="comp__legend-item">
              <span class="comp__dot" [style.background]="segment.color" aria-hidden="true"></span>
              <span class="comp__count">{{ segment.count }}</span>
              <span class="comp__label">{{ segment.label }}</span>
            </li>
          } @empty {
            <li class="comp__legend-item comp__legend-item--empty">No members yet</li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .comp {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .comp__bar {
      display: flex;
      width: 100%;
      height: 10px;
      border-radius: var(--radius-pill);
      overflow: hidden;
      background: rgba(255, 255, 255, 0.06);
    }

    .comp__segment {
      height: 100%;
      transition: width var(--transition-med);

      /* Hairline separators so adjacent segments stay distinguishable. */
      & + & {
        box-shadow: inset 1px 0 0 rgba(14, 21, 32, 0.55);
      }
    }

    .comp__segment--empty {
      width: 100%;
      background: rgba(255, 255, 255, 0.06);
    }

    .comp__legend {
      display: flex;
      flex-wrap: wrap;
      gap: 6px 18px;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .comp__legend-item {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      color: var(--tx-mut);
    }

    .comp__legend-item--empty {
      color: var(--tx-dim);
      font-style: italic;
    }

    .comp__dot {
      width: 8px;
      height: 8px;
      border-radius: 2px;
      flex: none;
    }

    .comp__count {
      font-family: var(--font-display);
      font-weight: 600;
      color: var(--tx);
    }
  `,
})
export class CompositionBar {
  readonly composition = input.required<Composition>();
  readonly showLegend = input(true);
}
