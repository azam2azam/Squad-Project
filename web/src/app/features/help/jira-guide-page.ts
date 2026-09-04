import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { MetadataService } from '../../core/services/metadata.service';

/**
 * The in-app guide to Jira sync.
 *
 * Open to every signed-in user, not just admins: the people who act on a suggestion are
 * Product Owners, and sending them to an admin to find out what "At Risk" means would
 * defeat the point. The admin-only parts are marked as such rather than hidden, so a PO
 * can see what to ask for.
 *
 * Content is static, but the page reads two live values — whether Jira is connected, and
 * whether the reader is an admin — so it can point at the next useful action instead of
 * describing one the reader cannot take.
 */
@Component({
  selector: 'app-jira-guide-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './jira-guide-page.html',
  styleUrl: './jira-guide-page.scss',
})
export class JiraGuidePage {
  private readonly auth = inject(AuthService);
  private readonly metadata = inject(MetadataService);

  protected readonly isAdmin = this.auth.isAdmin;
  protected readonly jiraEnabled = this.metadata.jiraSyncEnabled;

  /**
   * The status rules, in the order the server evaluates them. Kept as data rather than
   * markup so the table cannot drift out of order while someone edits the template.
   * Mirrors JiraClient.Summarise.
   */
  protected readonly statusRules = [
    { when: '20% or more of issues blocked', status: 'Blocked', token: '--status-blocked' },
    { when: 'Any blocked issue, under 20%', status: 'At Risk', token: '--status-at-risk' },
    { when: 'No issues found at all', status: 'On Track', token: '--status-on-track' },
    { when: '100% done, none blocked', status: 'Delivered', token: '--status-delivered' },
    { when: 'Anything else', status: 'On Track', token: '--status-on-track' },
  ];

  protected readonly writtenFields = ['Sprint', 'Progress', 'Status'];

  protected readonly protectedFields = [
    'Blocker note',
    'Risk level and note',
    'Squad members and roles',
    'Title and product',
    'Target date',
    'Velocity',
  ];
}
