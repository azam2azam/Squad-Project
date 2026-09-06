import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

export interface LineSeries {
  name: string;
  color: string;
  /** null means "no reading for that week" — drawn as a gap, never as zero. */
  values: (number | null)[];
}

interface Point {
  x: number;
  y: number;
  value: number;
  index: number;
}

interface RenderedSeries {
  name: string;
  color: string;
  /** One path per unbroken run, so a gap does not get bridged by a straight line. */
  segments: string[];
  points: Point[];
  last: Point | null;
}

/**
 * Multi-series line chart for progress over time.
 *
 * Two things it deliberately does not do. It never joins across a missing reading — a
 * gap is drawn as a gap, because bridging one asserts a measurement nobody took. And it
 * has a single y axis: a second scale is the fastest way to make two unrelated series
 * look correlated.
 *
 * Hand-built SVG rather than a charting library, matching the donut and bar charts, so
 * the whole app ships one rendering approach and no runtime dependency.
 */
@Component({
  selector: 'app-line-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <figure class="lc">
      <svg
        class="lc__svg"
        [attr.viewBox]="'0 0 ' + width + ' ' + height"
        role="img"
        [attr.aria-label]="ariaLabel()"
        (mouseleave)="hover.set(null)"
      >
        <!-- Grid first, so marks always sit above it. -->
        @for (line of gridLines(); track line.value) {
          <line
            [attr.x1]="padLeft"
            [attr.x2]="width - padRight"
            [attr.y1]="line.y"
            [attr.y2]="line.y"
            class="lc__grid"
          />
          <text [attr.x]="padLeft - 8" [attr.y]="line.y + 4" class="lc__axis lc__axis--y">
            {{ line.value }}{{ unit() }}
          </text>
        }

        @for (tick of xTicks(); track tick.index) {
          <text [attr.x]="tick.x" [attr.y]="height - 8" class="lc__axis lc__axis--x">
            {{ tick.label }}
          </text>
        }

        @for (s of rendered(); track s.name) {
          @for (segment of s.segments; track $index) {
            <path [attr.d]="segment" [attr.stroke]="s.color" class="lc__line" />
          }

          <!-- The endpoint is emphasised: on a trend, "where it ended up" is the reading. -->
          @if (s.last; as end) {
            <circle [attr.cx]="end.x" [attr.cy]="end.y" r="4" [attr.fill]="s.color" class="lc__end" />
          }
        }

        <!-- Hover column: a wide invisible target per week, bigger than the marks. -->
        @for (band of bands(); track band.index) {
          <rect
            [attr.x]="band.x"
            [attr.y]="0"
            [attr.width]="band.width"
            [attr.height]="height - 22"
            class="lc__band"
            (mouseenter)="hover.set(band.index)"
          />
        }

        @if (hover() !== null) {
          <line
            [attr.x1]="xAt(hover()!)"
            [attr.x2]="xAt(hover()!)"
            [attr.y1]="padTop"
            [attr.y2]="height - padBottom"
            class="lc__crosshair"
          />

          @for (s of rendered(); track s.name) {
            @for (p of s.points; track p.index) {
              @if (p.index === hover()) {
                <circle
                  [attr.cx]="p.x"
                  [attr.cy]="p.y"
                  r="4.5"
                  [attr.fill]="s.color"
                  class="lc__marker"
                />
              }
            }
          }
        }
      </svg>

      @if (hover() !== null) {
        <div class="lc__tip" role="status">
          <p class="lc__tip-head">{{ labels()[hover()!] }}</p>
          @for (row of tooltipRows(); track row.name) {
            <p class="lc__tip-row">
              <span class="lc__swatch" [style.background]="row.color"></span>
              <span class="lc__tip-name">{{ row.name }}</span>
              <span class="lc__tip-value">{{ row.text }}</span>
            </p>
          }
        </div>
      }

      <!-- A legend is always present for two or more series: identity never rests on
           colour alone. -->
      @if (series().length > 1) {
        <figcaption class="lc__legend">
          @for (s of series(); track s.name) {
            <span class="lc__legend-item">
              <span class="lc__swatch" [style.background]="s.color"></span>
              {{ s.name }}
            </span>
          }
        </figcaption>
      }
    </figure>
  `,
  styles: `
    :host {
      display: block;
    }

    .lc {
      margin: 0;
      position: relative;
    }

    .lc__svg {
      width: 100%;
      height: auto;
      display: block;
      overflow: visible;
    }

    .lc__grid {
      stroke: #e7ecf2;
      stroke-width: 1;
    }

    .lc__axis {
      fill: #8595a9;
      font-size: 10px;
      font-family: var(--font-ui);
    }

    .lc__axis--y {
      text-anchor: end;
    }

    .lc__axis--x {
      text-anchor: middle;
    }

    .lc__line {
      fill: none;
      stroke-width: 2;
      stroke-linecap: round;
      stroke-linejoin: round;
    }

    .lc__end,
    .lc__marker {
      stroke: var(--card);
      stroke-width: 2;
    }

    .lc__band {
      fill: transparent;
    }

    .lc__crosshair {
      stroke: #b6c2d1;
      stroke-width: 1;
      stroke-dasharray: 3 3;
    }

    .lc__tip {
      position: absolute;
      top: 0;
      right: 0;
      min-width: 170px;
      padding: 9px 11px;
      border: 1px solid var(--card-line);
      border-radius: var(--radius-sm);
      background: var(--card);
      box-shadow: var(--shadow-card);
      pointer-events: none;
      font-size: 12px;
    }

    .lc__tip-head {
      margin: 0 0 5px;
      font-weight: 700;
      color: var(--ink);
    }

    .lc__tip-row {
      display: flex;
      align-items: center;
      gap: 7px;
      margin: 0 0 2px;
    }

    .lc__tip-name {
      color: #4e5b6e;
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .lc__tip-value {
      font-weight: 700;
      font-variant-numeric: tabular-nums;
      color: var(--ink);
    }

    .lc__legend {
      display: flex;
      flex-wrap: wrap;
      gap: 6px 14px;
      margin-top: 10px;
      font-size: 11.5px;
      color: #4e5b6e;
    }

    .lc__legend-item {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }

    .lc__swatch {
      width: 9px;
      height: 9px;
      border-radius: 2px;
      flex: none;
    }
  `,
})
export class LineChart {
  readonly series = input.required<LineSeries[]>();
  readonly labels = input.required<string[]>();
  readonly unit = input('%');
  readonly max = input(100);
  readonly caption = input('');

  protected readonly hover = signal<number | null>(null);

  protected readonly width = 760;
  protected readonly height = 240;
  protected readonly padLeft = 34;
  protected readonly padRight = 12;
  protected readonly padTop = 12;
  protected readonly padBottom = 26;

  protected readonly ariaLabel = computed(() => {
    const names = this.series().map((s) => s.name).join(', ');
    return this.caption() || `Progress over time for ${names}`;
  });

  protected xAt(index: number): number {
    const count = this.labels().length;
    if (count <= 1) return this.padLeft;

    const span = this.width - this.padLeft - this.padRight;
    return this.padLeft + (span * index) / (count - 1);
  }

  private yAt(value: number): number {
    const span = this.height - this.padTop - this.padBottom;
    return this.padTop + span - (span * value) / this.max();
  }

  protected readonly gridLines = computed(() =>
    [0, 25, 50, 75, 100]
      .filter((v) => v <= this.max())
      .map((value) => ({ value, y: this.yAt(value) })),
  );

  /** Thinned so labels never collide, however many weeks are shown. */
  protected readonly xTicks = computed(() => {
    const labels = this.labels();
    const every = Math.max(1, Math.ceil(labels.length / 8));

    return labels
      .map((label, index) => ({ label, index, x: this.xAt(index) }))
      .filter((t) => t.index % every === 0 || t.index === labels.length - 1);
  });

  protected readonly bands = computed(() => {
    const count = this.labels().length;
    const span = this.width - this.padLeft - this.padRight;
    const step = count <= 1 ? span : span / (count - 1);

    return this.labels().map((_, index) => ({
      index,
      x: this.xAt(index) - step / 2,
      width: step,
    }));
  });

  protected readonly rendered = computed<RenderedSeries[]>(() =>
    this.series().map((s) => {
      const points: Point[] = [];
      const segments: string[] = [];
      let current: string[] = [];

      s.values.forEach((value, index) => {
        if (value === null || value === undefined) {
          // Close the run: the next reading starts a new path rather than a bridge.
          if (current.length > 1) segments.push(current.join(' '));
          current = [];
          return;
        }

        const point = { x: this.xAt(index), y: this.yAt(value), value, index };
        points.push(point);

        current.push(`${current.length === 0 ? 'M' : 'L'}${point.x} ${point.y}`);
      });

      if (current.length > 1) segments.push(current.join(' '));

      return {
        name: s.name,
        color: s.color,
        segments,
        points,
        last: points.length > 0 ? points[points.length - 1] : null,
      };
    }),
  );

  protected readonly tooltipRows = computed(() => {
    const index = this.hover();
    if (index === null) return [];

    return this.series().map((s) => {
      const value = s.values[index];

      return {
        name: s.name,
        color: s.color,
        // "No reading" is a distinct answer from zero, and the tooltip says which.
        text: value === null || value === undefined ? 'no reading' : `${value}${this.unit()}`,
      };
    });
  });
}
