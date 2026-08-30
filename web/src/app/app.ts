import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MetadataService } from './core/services/metadata.service';

/**
 * Application shell: the persistent header and the routed outlet.
 * Present mode and the export slide route render outside this chrome.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly metadata = inject(MetadataService);

  protected readonly serverExportEnabled = this.metadata.serverExportEnabled;
  protected readonly jiraSyncEnabled = this.metadata.jiraSyncEnabled;
  protected readonly roleCount = this.metadata.roles;
}
