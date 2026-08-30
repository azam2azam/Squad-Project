import { Component } from '@angular/core';
import { MilestonePlaceholder } from '../../shared/milestone-placeholder';

@Component({
  selector: 'app-roster-page',
  imports: [MilestonePlaceholder],
  template: `
    <app-milestone-placeholder
      milestone="Arriving in M3"
      title="Roster manager"
      description="The org-wide table of people with search and CRUD, so squad members are
        picked from a roster rather than retyped."
    />
  `,
})
export class RosterPage {}
