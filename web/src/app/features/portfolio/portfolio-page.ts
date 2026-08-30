import { Component } from '@angular/core';
import { MilestonePlaceholder } from '../../shared/milestone-placeholder';

@Component({
  selector: 'app-portfolio-page',
  imports: [MilestonePlaceholder],
  template: `
    <app-milestone-placeholder
      milestone="Arriving in M2"
      title="Portfolio"
      description="The exec-level grid of every board — title, squad, status and progress —
        lands with the Board CRUD API in M2."
    />
  `,
})
export class PortfolioPage {}
