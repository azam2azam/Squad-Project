import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

export interface StackSegment {
  label: string;
  color: string;
  count: number;
  /** Same-hue statuses get a hatch so they separate without relying on colour. */
  texture?: boolean;
}

export interface StackRow {
  name: string;
  /** Shown at the end of the row — the total the stack adds up to. */
  total: number;
  segments: StackSegment[];
}

/**
 * Horizontal stacked bars for comparing a mix across rows — delivery health per squad.
 *
 * Status colour is reserved and never doubles as a series hue, and every segment carries
 * its label in the legend and tooltip, so the reading never depends on colour alone.
 * Segments are separated by a 2px surface gap rather than a stroke, which keeps thin
 * slices visible without adding a mark of their own.
 */
@Component({
  selector: 'app-stacked-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <figure class="sb">
      <svg class="sb__defs" aria-hidden="true" width="0" height="0">
        <defs>
          <pattern id="sb-hatch" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
            <rect width="6" height="6" fill="rgba(255,255,255,0.34)" />
            <line x1="0" y1="0" x2="0" y2="6" stroke="rgba(0,0,0,0.28)" stroke-width="2.5" />
          </pattern>
        </defs>
      </svg>

      <div class="sb__rows">
        @for (row of rows(); track row.name) {
          <div class="sb__row">
            <p class="sb__name" [title]="row.name">{{ row.name }}</p>

            <div class="sb__track">
              @for (seg of segmentsOf(row); track seg.label) {
                <span
                  class="sb__seg"
                  [style.width.%]="seg.percent"
                  [style.background]="seg.color"
                  [title]="row.name + ' — ' + seg.count + ' ' + seg.label"
                  (mouseenter)="hover.set(row.name + '|' + seg.label)"
                  (mouseleave)="hover.set(null)"
                >
                  @if (seg.texture) {
                    <span class="sb__hatch"></span>
                  }
                  <!-- Only wide enough slices are direct-labelled; the rest read from
                       the legend and the tooltip. -->
                  @if (seg.percent >= 14) {
                    <span class="sb__seg-count">{{ seg.count }}</span>
                  }
                </span>
              }
            </div>

            <p class="sb__total">{{ row.total }}</p>
          </div>
        }
      </div>

      <figcaption class="sb__legend">
        @for (item of legend(); track item.label) {
          <span class="sb__legend-item">
            <span class="sb__swatch" [style.background]="item.color">
              @if (item.texture) {
                <span class="sb__hatch"></span>
              }
            </span>
            {{ item.label }}
          </span>
        }
      </figcaption>
    </figure>
  `,
  styles: `
    :host {
      display: block;
    }

    .sb {
      margin: 0;
    }

    .sb__defs {
      position: absolute;
    }

    .sb__rows {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .sb__row {
      display: grid;
      grid-template-columns: minmax(90px, 150px) minmax(0, 1fr) 30px;
      align-items: center;
      gap: 10px;
    }

    .sb__name {
      margin: 0;
      font-size: 12px;
      color: #4e5b6e;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .sb__track {
      display: flex;
      height: 18px;
      border-radius: 4px;
      overflow: hidden;
      background: #eef2f6;
    }

    .sb__seg {
      position: relative;
      display: flex;
      align-items: center;
      justify-content: center;
      min-width: 3px;
      transition: filter 120ms ease;

      /* A 2px surface gap keeps adjacent fills apart without adding a stroke. */
      & + & {
        box-shadow: -2px 0 0 var(--card);
      }

      &:hover {
        filter: brightness(1.06);
      }
    }

    .sb__hatch {
      position: absolute;
      inset: 0;
      background: url(#sb-hatch);
    }

    .sb__seg-count {
      position: relative;
      font-size: 10px;
      font-weight: 700;
      color: #0e1520;
      font-variant-numeric: tabular-nums;
    }

    .sb__total {
      margin: 0;
      font-size: 11.5px;
      font-weight: 700;
      color: #59687e;
      text-align: right;
      font-variant-numeric: tabular-nums;
    }

    .sb__legend {
      display: flex;
      flex-wrap: wrap;
      gap: 6px 14px;
      margin-top: 12px;
      font-size: 11.5px;
      color: #4e5b6e;
    }

    .sb__legend-item {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }

    .sb__swatch {
      position: relative;
      width: 9px;
      height: 9px;
      border-radius: 2px;
      overflow: hidden;
      flex: none;
    }
  `,
})
export class StackedBarChart {
  readonly rows = input.required<StackRow[]>();

  protected readonly hover = signal<string | null>(null);

  protected segmentsOf(row: StackRow) {
    const total = row.segments.reduce((n, s) => n + s.count, 0);

    return row.segments
      .filter((s) => s.count > 0)
      .map((s) => ({
        ...s,
        percent: total === 0 ? 0 : (s.count * 100) / total,
      }));
  }

  /** One entry per status actually present, in the order the rows use them. */
  protected readonly legend = computed(() => {
    const seen = new Map<string, StackSegment>();

    for (const row of this.rows()) {
      for (const seg of row.segments) {
        if (seg.count > 0 && !seen.has(seg.label)) seen.set(seg.label, seg);
      }
    }

    return [...seen.values()];
  });
}
