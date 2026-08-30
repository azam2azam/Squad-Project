import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { Composition } from '../../core/models/board.models';

/**
 * Squad composition: a "Squad composition / N people" header, a stacked bar, and the
 * legend beneath it. Transcribed from the prototype — 12px bar on a --panel-2 track
 * with 2px gaps between segments, and legend counts set in the display face.
 *
 * Widths come from the server-computed percentages, which are guaranteed to sum to
 * 100, so the bar cannot end with a sliver of track from rounding. Counts are rendered
 * as text, so the information is never carried by colour alone.
 */
@Component({
  selector: 'app-composition-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="comp">
      @if (showHeading()) {
        <div class="comp__top">
          <span class="comp__h">Squad composition</span>
          <span class="comp__n">{{ headingCount() }}</span>
        </div>
      }

      <div
        class="comp__bar"
        role="img"
        [attr.aria-label]="'Squad composition: ' + composition().legendText"
      >
        @for (segment of composition().segments; track segment.role) {
          <span
            class="comp__seg"
            [style.width.%]="segment.percent"
            [style.background]="segment.color"
            [title]="segment.count + ' × ' + segment.label"
          ></span>
        } @empty {
          <span class="comp__seg comp__seg--empty"></span>
        }
      </div>

      @if (showLegend()) {
        <ul class="comp__legend">
          @for (segment of composition().segments; track segment.role) {
            <li class="comp__lg">
              <span class="comp__sw" [style.background]="segment.color" aria-hidden="true"></span>
              <b>{{ segment.count }}</b>
              {{ segment.count === 1 ? segment.label : segment.pluralLabel }}
            </li>
          } @empty {
            <li class="comp__lg comp__lg--empty">No members yet</li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .comp {
      width: 100%;
      min-width: 0;
    }

    .comp__top {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      margin-bottom: 9px;
    }

    .comp__h {
      font-size: 11.5px;
      font-weight: 550;
      letter-spacing: 0.09em;
      text-transform: uppercase;
      color: var(--tx-mut);
    }

    .comp__n {
      font-family: var(--font-display);
      font-size: 14px;
      font-weight: 600;
    }

    .comp__bar {
      display: flex;
      gap: 2px;
      height: 12px;
      border-radius: 7px;
      overflow: hidden;
      background: var(--panel-2);
    }

    .comp__seg {
      height: 100%;
      transition: width 0.3s;
    }

    .comp__seg--empty {
      width: 100%;
      background: var(--panel-2);
    }

    .comp__legend {
      display: flex;
      flex-wrap: wrap;
      gap: 11px 16px;
      margin: 12px 0 0;
      padding: 0;
      list-style: none;
    }

    .comp__lg {
      display: flex;
      align-items: center;
      gap: 7px;
      font-size: 12px;
      color: var(--tx-mut);

      b {
        color: var(--tx);
        font-family: var(--font-display);
        font-weight: 600;
      }
    }

    .comp__lg--empty {
      font-style: italic;
      color: var(--tx-dim);
    }

    .comp__sw {
      width: 9px;
      height: 9px;
      border-radius: 3px;
      flex: none;
    }
  `,
})
export class CompositionBar {
  readonly composition = input.required<Composition>();
  readonly showLegend = input(true);
  readonly showHeading = input(true);

  /** e.g. "6 people" — supplied by the slide so the count is phrased in one place. */
  readonly headingCount = input('');
}
