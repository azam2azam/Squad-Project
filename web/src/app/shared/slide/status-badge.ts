import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Colour-coded delivery status pill. The colour comes from the server's status metadata
 * rather than a local map, so the badge and a server-rendered export cannot disagree.
 * The label is always present as text — colour alone never carries the meaning.
 */
@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="badge" [style.--badge-color]="color()">
      <span class="badge__dot" aria-hidden="true"></span>
      <span class="badge__label">{{ label() }}</span>
    </span>
  `,
  styles: `
    .badge {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 6px 14px 6px 12px;
      border-radius: var(--radius-pill);
      font-size: 13px;
      font-weight: 600;
      letter-spacing: 0.01em;
      white-space: nowrap;
      color: var(--badge-color);
      /* Tinted fill rather than a solid one, so the badge sits on the dark slide
         without shouting louder than the title. */
      background: color-mix(in srgb, var(--badge-color) 14%, transparent);
      border: 1px solid color-mix(in srgb, var(--badge-color) 38%, transparent);
    }

    .badge__dot {
      width: 7px;
      height: 7px;
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
