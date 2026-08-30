import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { withAlpha } from './color';

/**
 * Colour-coded delivery status pill, transcribed from the prototype: a solid dot,
 * the label, a 10%-alpha tint of the status colour as the fill and 33% as the border.
 *
 * The tints are computed to rgba() rather than written as `color-mix()` so the PNG
 * exporter can parse them — see color.ts.
 *
 * The colour comes from the server's status metadata rather than a local map, so the
 * badge and a server-rendered export cannot disagree. The label is always present as
 * text — colour alone never carries the meaning.
 */
@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="badge"
      [style.color]="color()"
      [style.background]="fill()"
      [style.border-color]="stroke()"
    >
      <span class="badge__pulse" [style.background]="color()" aria-hidden="true"></span>
      <span class="badge__label">{{ label() }}</span>
    </span>
  `,
  styles: `
    .badge {
      flex: 0 0 auto;
      font-size: 12px;
      font-weight: 600;
      padding: 7px 13px;
      border-radius: 20px;
      display: inline-flex;
      align-items: center;
      gap: 7px;
      white-space: nowrap;
      border: 1px solid transparent;
    }

    .badge__pulse {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      flex: none;
    }
  `,
})
export class StatusBadge {
  readonly label = input.required<string>();
  readonly color = input.required<string>();

  protected readonly fill = computed(() => withAlpha(this.color(), 0.1));
  protected readonly stroke = computed(() => withAlpha(this.color(), 0.33));
}
