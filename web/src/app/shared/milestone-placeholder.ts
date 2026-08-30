import { Component, input } from '@angular/core';

/**
 * Honest stand-in for a screen that a later milestone fills in. Kept as one
 * component so no half-built screen pretends to be finished, and so every
 * placeholder disappears in a single deletion when its milestone lands.
 */
@Component({
  selector: 'app-milestone-placeholder',
  template: `
    <section class="placeholder">
      <p class="placeholder__milestone">{{ milestone() }}</p>
      <h1 class="placeholder__title">{{ title() }}</h1>
      <p class="placeholder__body">{{ description() }}</p>
    </section>
  `,
  styles: `
    .placeholder {
      max-width: 560px;
      margin: 80px auto;
      padding: 32px;
      background: var(--card);
      border: 1px solid var(--card-line);
      border-radius: var(--radius-lg);
      box-shadow: var(--shadow-card);
      text-align: center;
    }

    .placeholder__milestone {
      margin: 0 0 8px;
      font-size: 11px;
      font-weight: 600;
      letter-spacing: 0.1em;
      text-transform: uppercase;
      color: var(--accent-deep);
    }

    .placeholder__title {
      font-size: 24px;
      margin-bottom: 10px;
    }

    .placeholder__body {
      margin: 0;
      color: #5d6b7e;
    }
  `,
})
export class MilestonePlaceholder {
  readonly milestone = input.required<string>();
  readonly title = input.required<string>();
  readonly description = input.required<string>();
}
