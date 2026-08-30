import { Component } from '@angular/core';
import { MilestonePlaceholder } from '../../shared/milestone-placeholder';

@Component({
  selector: 'app-board-editor-page',
  imports: [MilestonePlaceholder],
  template: `
    <app-milestone-placeholder
      milestone="Arriving in M2"
      title="Board editor"
      description="The builder form and the live sticky slide — progress ring, composition bar
        and member cards — are the core of M2."
    />
  `,
})
export class BoardEditorPage {}
