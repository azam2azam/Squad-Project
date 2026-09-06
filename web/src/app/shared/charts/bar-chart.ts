import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface BarDatum {
  label: string;
  value: number;
  /** Optional second line under the label, e.g. "3 boards · 6 people". */
  detail?: string;
}

/**
 * Horizontal bars for one measure compared across categories.
 *
 * Deliberately a single hue, not one colour per squad: the bars encode **magnitude**,
 * and colouring by category here would imply an identity the data does not have — and
 * would repaint when the list is filtered. Every bar is directly labelled with its
 * value, so no legend is needed and no value is read off an axis.
 */
@Component({
  selector: 'app-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.bars--on-light]': 'surface() === "light"' },
  template: `
    <figure class="bars">
      @for (bar of bars(); track bar.label) {
        <div class="bars__row">
          <div class="bars__head">
            <span class="bars__label">{{ bar.label }}</span>
            @if (bar.detail) {
              <span class="bars__detail">{{ bar.detail }}</span>
            }
          </div>

          <div
            class="bars__track"
            role="img"
            [attr.aria-label]="bar.label + ': ' + bar.value + unit()"
          >
            <span class="bars__fill" [style.width.%]="bar.width" [style.background]="color()">
            </span>
          </div>

          <span class="bars__value">{{ bar.value }}{{ unit() }}</span>
        </div>
      } @empty {
        <p class="bars__empty">Nothing to compare yet.</p>
      }
    </figure>
  `,
  styles: `
    .bars {
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .bars__row {
      display: grid;
      grid-template-columns: minmax(90px, 150px) 1fr auto;
      align-items: center;
      gap: 12px;
    }

    .bars__head {
      min-width: 0;
      display: flex;
      flex-direction: column;
    }

    .bars__label {
      font-size: 12.5px;
      font-weight: 600;
      color: var(--tx);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .bars__detail {
      font-size: 11px;
      color: var(--tx-dim);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .bars__track {
      height: 10px;
      border-radius: 5px;
      background: var(--panel-2);
      overflow: hidden;
    }

    .bars__fill {
      display: block;
      height: 100%;
      /* Rounded data-end, anchored flat to the baseline. */
      border-radius: 0 4px 4px 0;
      min-width: 2px;
      transition: width var(--transition-med);
    }

    .bars__value {
      font-family: var(--font-display);
      font-size: 13px;
      font-weight: 600;
      color: var(--tx);
      min-width: 44px;
      text-align: right;
    }

    .bars__empty {
      margin: 0;
      font-size: 12.5px;
      color: var(--tx-dim);
      font-style: italic;
    }

    /*
     * The defaults above are the dark dashboard panels this chart was built for.
     * On a light card those tokens invert into near-white text on a black track, so a
     * host that sits on a light surface asks for this variant instead.
     */
    :host(.bars--on-light) .bars__label,
    :host(.bars--on-light) .bars__value {
      color: var(--ink-tx);
    }

    :host(.bars--on-light) .bars__detail,
    :host(.bars--on-light) .bars__empty {
      color: #5d6b7e;
    }

    :host(.bars--on-light) .bars__track {
      background: #e7ecf2;
    }
  `,
})
export class BarChart {
  readonly data = input.required<BarDatum[]>();
  readonly unit = input('%');
  readonly color = input('var(--accent)');

  /**
   * Overrides the scale. Needed where a percentage can legitimately exceed 100 — someone
   * allocated 150% across squads must not draw the same full bar as someone at 100%,
   * because being over-committed is the whole point of looking.
   */
  readonly max = input<number | null>(null);

  /** The surface the chart is placed on, so its text and track stay legible. */
  readonly surface = input<'dark' | 'light'>('dark');

  /** Bars scale to the largest value, or to 100 when the unit is a percentage. */
  protected readonly bars = computed(() => {
    const data = this.data();
    const max =
      this.max() ?? (this.unit() === '%' ? 100 : Math.max(1, ...data.map((d) => d.value)));

    return data.map((d) => ({
      ...d,
      width: Math.max(0, Math.min(100, (d.value / max) * 100)),
    }));
  });
}
