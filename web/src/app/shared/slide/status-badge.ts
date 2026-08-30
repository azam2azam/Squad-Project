import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Colour-coded delivery status pill, transcribed from the prototype: a solid dot,
 * the label, a 1A-alpha tint of the status colour as the fill and 55-alpha as the
 * border.
 *
 * The colour comes from the server's status metadata rather than a local map, so the
 * badge and a server-rendered export cannot disagree. The label is always present as
 * text — colour alone never carries the meaning.
 */
@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="badge" [style.--badge-color]="color()">
      <span class="badge__pulse" aria-hidden="true"></span>
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
      color: var(--badge-color);
      background: color-mix(in srgb, var(--badge-color) 10%, transparent);
      border: 1px solid color-mix(in srgb, var(--badge-color) 33%, transparent);
    }

    .badge__pulse {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--badge-color);
      flex: none;
    }
  `,
})
export class StatusBadge {
  readonly label = input.required<string>();
  readonly color = input.required<string>();
}
