import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Circular progress indicator built from a conic-gradient, as in the prototype.
 *
 * The percentage is exposed as real text inside the ring, so it is legible to screen
 * readers without a separate description — the visual and the accessible name are the
 * same number rather than two things that can drift apart.
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
      [style.--ring-thickness.px]="thickness()"
      [style.--ring-sweep]="sweep()"
      [style.--ring-color]="color()"
    >
      <div class="ring__track" aria-hidden="true"></div>
      <div class="ring__hole" aria-hidden="true">
        <span class="ring__value">{{ percent() }}<span class="ring__unit">%</span></span>
        @if (caption()) {
          <span class="ring__caption">{{ caption() }}</span>
        }
      </div>
    </div>
  `,
  styles: `
    .ring {
      position: relative;
      width: var(--ring-size);
      height: var(--ring-size);
      flex: none;
      display: grid;
      place-items: center;
    }

    .ring__track {
      position: absolute;
      inset: 0;
      border-radius: 50%;
      /* The sweep is the filled arc; the remainder is the unfilled track. */
      background: conic-gradient(var(--ring-color) var(--ring-sweep), rgba(255, 255, 255, 0.08) 0);
      transition: background var(--transition-med);
    }

    .ring__hole {
      position: relative;
      width: calc(var(--ring-size) - var(--ring-thickness) * 2);
      height: calc(var(--ring-size) - var(--ring-thickness) * 2);
      border-radius: 50%;
      background: var(--panel);
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 2px;
    }

    .ring__value {
      font-family: var(--font-display);
      font-weight: 600;
      font-size: calc(var(--ring-size) * 0.26);
      line-height: 1;
      color: var(--tx);
      letter-spacing: -0.02em;
    }

    .ring__unit {
      font-size: 0.5em;
      color: var(--tx-mut);
      margin-left: 1px;
    }

    .ring__caption {
      font-size: calc(var(--ring-size) * 0.085);
      letter-spacing: 0.1em;
      text-transform: uppercase;
      color: var(--tx-dim);
    }
  `,
})
export class ProgressRing {
  readonly percent = input.required<number>();
  readonly size = input(140);
  readonly thickness = input(10);
  readonly color = input('var(--accent)');
  readonly caption = input<string | null>('Complete');

  /** Clamped so a bad value can never render a partially-filled or over-full ring. */
  protected readonly sweep = computed(() => {
    const clamped = Math.max(0, Math.min(100, this.percent()));
    return `${clamped * 3.6}deg`;
  });
}
