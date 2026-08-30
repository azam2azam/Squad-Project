import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

export interface DonutSlice {
  label: string;
  value: number;
  color: string;
  /**
   * Draws a diagonal hatch over the slice. Used where two slices share a hue —
   * On Track and Delivered differ by only ΔE 5.2, which is under the readable floor,
   * so colour alone cannot carry their identity.
   */
  texture?: boolean;
}

interface Arc extends DonutSlice {
  path: string;
  percent: number;
  midAngle: number;
}

/**
 * Donut for a part-to-whole breakdown of a small number of categories.
 *
 * Identity is never colour-alone: every slice is in the legend with its label and
 * count, adjacent slices are separated by a surface-coloured gap, and a slice can
 * carry a texture. Hovering a slice raises it and names it in the centre.
 */
@Component({
  selector: 'app-donut-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="donut">
      <div class="donut__plot">
        <svg [attr.viewBox]="'0 0 ' + size + ' ' + size" role="img" [attr.aria-label]="summary()">
          <defs>
            <!-- 45° hatch, used as secondary encoding on same-hue slices. -->
            <pattern
              id="donut-hatch"
              width="6"
              height="6"
              patternUnits="userSpaceOnUse"
              patternTransform="rotate(45)"
            >
              <rect width="6" height="6" fill="transparent" />
              <line x1="0" y1="0" x2="0" y2="6" stroke="var(--ink)" stroke-width="2.5" />
            </pattern>
          </defs>

          @for (arc of arcs(); track arc.label) {
            <path
              [attr.d]="arc.path"
              [attr.fill]="arc.color"
              class="donut__arc"
              [class.is-active]="active() === arc.label"
              (mouseenter)="active.set(arc.label)"
              (mouseleave)="active.set(null)"
            >
              <title>{{ arc.label }}: {{ arc.value }} ({{ arc.percent }}%)</title>
            </path>
            @if (arc.texture) {
              <path [attr.d]="arc.path" fill="url(#donut-hatch)" class="donut__hatch" />
            }
          }
        </svg>

        <div class="donut__center">
          @if (activeArc(); as arc) {
            <span class="donut__value">{{ arc.value }}</span>
            <span class="donut__label">{{ arc.label }}</span>
          } @else {
            <span class="donut__value">{{ total() }}</span>
            <span class="donut__label">{{ centerLabel() }}</span>
          }
        </div>
      </div>

      <!-- Always present: with more than one series, identity must not rely on hue. -->
      <ul class="donut__legend">
        @for (arc of arcs(); track arc.label) {
          <li class="donut__legend-item" [class.is-active]="active() === arc.label">
            <span class="donut__swatch" [style.background]="arc.color" aria-hidden="true">
              @if (arc.texture) {
                <span class="donut__swatch-hatch"></span>
              }
            </span>
            <span class="donut__legend-label">{{ arc.label }}</span>
            <b class="donut__legend-value">{{ arc.value }}</b>
          </li>
        }
      </ul>
    </figure>
  `,
  styles: `
    .donut {
      margin: 0;
      display: flex;
      align-items: center;
      gap: 20px;
      flex-wrap: wrap;
    }

    .donut__plot {
      position: relative;
      width: 168px;
      height: 168px;
      flex: none;
    }

    svg {
      width: 100%;
      height: 100%;
      overflow: visible;
    }

    .donut__arc {
      /* A surface-coloured gap between segments, per the mark spec. */
      stroke: var(--panel);
      stroke-width: 2;
      transition:
        opacity var(--transition-fast),
        transform var(--transition-fast);
      transform-origin: center;
      cursor: default;
    }

    .donut__arc.is-active {
      transform: scale(1.03);
    }

    .donut__hatch {
      pointer-events: none;
      stroke: var(--panel);
      stroke-width: 2;
      opacity: 0.5;
    }

    .donut__center {
      position: absolute;
      inset: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      pointer-events: none;
      text-align: center;
    }

    .donut__value {
      font-family: var(--font-display);
      font-size: 30px;
      font-weight: 700;
      line-height: 1;
      color: var(--tx);
    }

    .donut__label {
      font-size: 10.5px;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--tx-mut);
      margin-top: 3px;
      max-width: 96px;
    }

    .donut__legend {
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: 7px;
      min-width: 0;
    }

    .donut__legend-item {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12.5px;
      /* Text wears text tokens; the swatch beside it carries identity. */
      color: var(--tx-mut);
      opacity: 0.75;
      transition: opacity var(--transition-fast);
    }

    .donut__legend-item.is-active {
      opacity: 1;
    }

    .donut__swatch {
      position: relative;
      width: 10px;
      height: 10px;
      border-radius: 3px;
      flex: none;
      overflow: hidden;
    }

    .donut__swatch-hatch {
      position: absolute;
      inset: 0;
      background: repeating-linear-gradient(45deg, transparent 0 2px, var(--ink) 2px 3.5px);
    }

    .donut__legend-value {
      margin-left: auto;
      font-family: var(--font-display);
      color: var(--tx);
    }
  `,
})
export class DonutChart {
  readonly slices = input.required<DonutSlice[]>();
  readonly centerLabel = input('Total');

  protected readonly size = 168;
  protected readonly active = signal<string | null>(null);

  protected readonly total = computed(() => this.slices().reduce((sum, s) => sum + s.value, 0));

  protected readonly activeArc = computed(
    () => this.arcs().find((a) => a.label === this.active()) ?? null,
  );

  protected readonly summary = computed(
    () =>
      `Breakdown: ${this.slices()
        .map((s) => `${s.label} ${s.value}`)
        .join(', ')}`,
  );

  protected readonly arcs = computed<Arc[]>(() => {
    const slices = this.slices().filter((s) => s.value > 0);
    const total = slices.reduce((sum, s) => sum + s.value, 0);
    if (total === 0) return [];

    const cx = this.size / 2;
    const cy = this.size / 2;
    const outer = this.size / 2 - 4;
    const inner = outer * 0.62;

    let angle = -Math.PI / 2; // start at 12 o'clock

    return slices.map((slice) => {
      const sweep = (slice.value / total) * Math.PI * 2;
      const end = angle + sweep;
      const path = arcPath(cx, cy, inner, outer, angle, end);
      const mid = angle + sweep / 2;
      angle = end;

      return {
        ...slice,
        path,
        percent: Math.round((slice.value / total) * 100),
        midAngle: mid,
      };
    });
  });
}

/** Ring segment between two angles, as an SVG path. */
function arcPath(
  cx: number,
  cy: number,
  inner: number,
  outer: number,
  start: number,
  end: number,
): string {
  // A full circle cannot be drawn with a single arc — nudge it just short.
  const sweep = end - start;
  const adjustedEnd = sweep >= Math.PI * 2 ? end - 0.0001 : end;
  const large = adjustedEnd - start > Math.PI ? 1 : 0;

  const ox1 = cx + outer * Math.cos(start);
  const oy1 = cy + outer * Math.sin(start);
  const ox2 = cx + outer * Math.cos(adjustedEnd);
  const oy2 = cy + outer * Math.sin(adjustedEnd);
  const ix2 = cx + inner * Math.cos(adjustedEnd);
  const iy2 = cy + inner * Math.sin(adjustedEnd);
  const ix1 = cx + inner * Math.cos(start);
  const iy1 = cy + inner * Math.sin(start);

  return [
    `M ${ox1} ${oy1}`,
    `A ${outer} ${outer} 0 ${large} 1 ${ox2} ${oy2}`,
    `L ${ix2} ${iy2}`,
    `A ${inner} ${inner} 0 ${large} 0 ${ix1} ${iy1}`,
    'Z',
  ].join(' ');
}
