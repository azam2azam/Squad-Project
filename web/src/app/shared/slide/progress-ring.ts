import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Circular progress indicator, transcribed from the prototype: a 150px conic-gradient
 * disc with an inset hole punched by a pseudo-element, filled in --accent against a
 * --panel-2 track.
 *
 * The percentage is real text inside the ring, so the visual and the accessible name
 * are the same number rather than two things that can drift apart.
 */
@Component({
  selector: 'app-progress-ring',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="ring"
      role="img"
      [attr.aria-label]="'Progress: ' + percent() + ' percent complete'"
      [style.--ring-size.px]="size()"
      [style.--ring-sweep]="sweep()"
      [style.--ring-color]="color()"
    >
      <div class="ring__core">
        <div class="ring__pct">{{ percent() }}%</div>
        @if (caption()) {
          <div class="ring__lbl">{{ caption() }}</div>
        }
      </div>
    </div>
  `,
  styles: `
    .ring {
      width: var(--ring-size);
      height: var(--ring-size);
      border-radius: 50%;
      display: grid;
      place-items: center;
      position: relative;
      flex: 0 0 auto;
      background: conic-gradient(var(--ring-color) var(--ring-sweep), var(--panel-2) 0deg);
      transition: background var(--transition-med);
    }

    /* Punches the hole, leaving a 13px band as the ring itself. */
    .ring::after {
      content: '';
      position: absolute;
      inset: 13px;
      border-radius: 50%;
      background: var(--ink);
    }

    .ring__core {
      position: relative;
      z-index: 2;
      text-align: center;
    }

    .ring__pct {
      font-family: var(--font-display);
      font-size: 38px;
      font-weight: 700;
      line-height: 1;
    }

    .ring__lbl {
      font-size: 10.5px;
      letter-spacing: 0.1em;
      text-transform: uppercase;
      color: var(--tx-mut);
      margin-top: 3px;
    }
  `,
})
export class ProgressRing {
  readonly percent = input.required<number>();
  readonly size = input(150);
  readonly color = input('var(--accent)');
  readonly caption = input<string | null>('Complete');

  /** Clamped so a bad value can never render a partially-filled or over-full ring. */
  protected readonly sweep = computed(() => {
    const clamped = Math.max(0, Math.min(100, this.percent()));
    return `${clamped * 3.6}deg`;
  });
}
