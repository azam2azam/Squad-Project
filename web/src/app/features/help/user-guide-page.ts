import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { MetadataService } from '../../core/services/metadata.service';

/**
 * The manual for the whole application, in the application.
 *
 * Open to everyone signed in. Admin-only screens are marked with a chip rather than
 * hidden: a Product Owner who can see that categories exist knows what to ask an admin
 * for, whereas a manual that silently omits half the product makes the product look
 * smaller than it is.
 *
 * Content is static prose, but the header reads live state — the reader's role, and
 * whether the two integrations are connected — so the page can point at the next useful
 * action instead of describing one this reader cannot take.
 */
@Component({
  selector: 'app-user-guide-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './user-guide-page.html',
  styleUrls: ['./guide.scss', './user-guide-page.scss'],
})
export class UserGuidePage {
  private readonly auth = inject(AuthService);
  private readonly metadata = inject(MetadataService);

  protected readonly isAdmin = this.auth.isAdmin;
  protected readonly canWrite = this.auth.canWrite;
  protected readonly jiraEnabled = this.metadata.jiraSyncEnabled;
  protected readonly smartsheetEnabled = this.metadata.smartsheetSyncEnabled;

  protected readonly roleLabel = computed(() => {
    const role = this.auth.user()?.roleName;
    return role === 'ProductOwner' ? 'Product Owner' : (role ?? 'Viewer');
  });

  /**
   * What each role may do, in the order the roles widen. Kept as data rather than markup
   * so a row cannot drift out of order while somebody edits the template. Mirrors the
   * API policies, which are what actually enforce this — the UI only hides the buttons.
   */
  protected readonly permissions = [
    {
      action: 'View boards, schedule, dashboard and analytics',
      viewer: true,
      po: true,
      admin: true,
    },
    { action: 'Present and export (PNG, PDF)', viewer: true, po: true, admin: true },
    { action: 'Create and edit boards', viewer: false, po: true, admin: true },
    { action: 'Add squad members and tasks', viewer: false, po: true, admin: true },
    { action: 'Pull a Jira or Smartsheet suggestion', viewer: false, po: true, admin: true },
    { action: 'Excel and JSON import', viewer: false, po: false, admin: true },
    { action: 'The roster, and recording time off', viewer: false, po: false, admin: true },
    { action: 'Categories, roles, users, integrations', viewer: false, po: false, admin: true },
  ];

  /** The board fields, as the editor orders them down the page. */
  protected readonly boardFields = [
    {
      name: 'Project title',
      detail: 'What the work is called. This is the headline on the slide.',
    },
    {
      name: 'Product',
      detail: 'The module or system it touches — Discharge, Admission, jQuery Removal.',
    },
    { name: 'Sprint', detail: 'Optional. The sprint or iteration currently in flight.' },
    {
      name: 'Squad',
      detail:
        'The team name as you want it read out — "Pradeep & Shehan". Squads are grouped by ' +
        'this text, so spell it the same way each time or you will get two squads.',
    },
    {
      name: 'Status',
      detail:
        'On Track, At Risk, Blocked, In Review or Delivered. Drives the colour on the slide ' +
        'and every count on the dashboard.',
    },
    { name: 'Progress', detail: 'Nought to a hundred. The ring on the slide.' },
    {
      name: 'Blocker note',
      detail:
        'What is actually stopping the work. Setting the status to Blocked without one ' +
        'raises a warning, because "Blocked" with no reason tells a reviewer to worry ' +
        'without saying what about.',
    },
    {
      name: 'Risk level',
      detail: 'No risk through Critical. Separate from status: work can be on track and risky.',
    },
    {
      name: 'Risk note',
      detail: 'Required in practice from Medium up — the same warning rule as the blocker.',
    },
    {
      name: 'Programme',
      detail: 'Which category this board belongs to. Uncategorised is a real, allowed state.',
    },
    {
      name: 'Jira / Smartsheet',
      detail: 'Where this board reads its figures from, if it reads them from anywhere.',
    },
  ];

  /** The Boards sheet, column for column, as the importer expects it. */
  protected readonly excelColumns = [
    { name: 'Id', note: 'Leave blank for a new board. Keep it to update an existing one.' },
    { name: 'Title', note: 'Required.' },
    { name: 'Product', note: 'Required.' },
    { name: 'Squad', note: 'Required. The grouping name.' },
    { name: 'Sprint', note: 'Optional.' },
    { name: 'Status', note: 'On Track, At Risk, Blocked, In Review, Delivered.' },
    { name: 'Progress %', note: '0–100.' },
    { name: 'Risk', note: 'No risk, Low, Medium, High, Critical.' },
    { name: 'Risk note', note: 'Optional.' },
    { name: 'Blocker note', note: 'Optional.' },
    { name: 'Target date', note: 'Optional.' },
    { name: 'Jira project key', note: 'Optional, e.g. VIDA.' },
    { name: 'Jira board id', note: 'Optional.' },
    { name: 'Order', note: 'Where it sits in the portfolio.' },
    { name: 'Category', note: 'The programme name. A name nobody has used yet is created.' },
  ];
}
