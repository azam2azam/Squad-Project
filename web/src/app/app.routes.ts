import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './core/auth.guard';

/**
 * Feature routes are lazy so Present mode and the export-only slide route load in
 * isolation without pulling in the builder.
 *
 * Guards mirror the API policies (spec section 8). They are a convenience — the server
 * enforces the same rules, so bypassing the router gains nothing.
 */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'dashboard',
  },
  {
    path: 'dashboard',
    title: 'Delivery overview · Squad Status Board',
    canActivate: [authGuard],
    loadComponent: () => import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage),
  },
  {
    path: 'login',
    title: 'Sign in · Squad Status Board',
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'portfolio',
    title: 'Portfolio · Squad Status Board',
    canActivate: [authGuard],
    loadComponent: () => import('./features/portfolio/portfolio-page').then((m) => m.PortfolioPage),
  },
  {
    path: 'boards/:id',
    title: 'Board editor · Squad Status Board',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/boards/board-editor-page').then((m) => m.BoardEditorPage),
  },
  {
    // Roster is org-wide, so only an Admin may open it — matching the API, which
    // refuses roster writes from anyone else.
    path: 'roster',
    title: 'Roster · Squad Status Board',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/roster/roster-page').then((m) => m.RosterPage),
  },
  {
    // Analytics is read-only comparison, so any signed-in user may open it — reading
    // this is precisely a Viewer's job.
    path: 'analytics',
    title: 'Analytics · Squad Status Board',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/analytics/analytics-page').then((m) => m.AnalyticsPage),
  },
  {
    // Roles are org-wide reference data that change what every board renders, so only an
    // admin may edit them — matching the API, which enforces it in the handlers.
    path: 'settings/roles',
    title: 'Roles · Squad Status Board',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/roles/roles-page').then((m) => m.RolesPage),
  },
  {
    // Accounts that can sign in. Admin-only, matching the API, which enforces the same
    // rules in its handlers rather than relying on this guard.
    path: 'settings/users',
    title: 'Users · Squad Status Board',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/users/users-page').then((m) => m.UsersPage),
  },
  {
    // Everyone may change their own password — that is what makes an admin-set password
    // acceptable, so this one is not admin-guarded.
    path: 'settings/password',
    title: 'Change password · Squad Status Board',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/users/change-password-page').then((m) => m.ChangePasswordPage),
  },
  {
    // Open to everyone signed in, unlike the settings screen: the people who act on a
    // Jira suggestion are Product Owners, and the admin-only parts are marked rather
    // than hidden so a PO can see what to ask for.
    path: 'help/jira-sync',
    title: 'Jira sync guide · Squad Status Board',
    canActivate: [authGuard],
    loadComponent: () => import('./features/help/jira-guide-page').then((m) => m.JiraGuidePage),
  },
  {
    // The Jira connection acts on behalf of the whole org and holds a credential,
    // so it is Admin-only — matching the API, which refuses these routes to anyone else.
    path: 'settings/jira',
    title: 'Jira connection · Squad Status Board',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./features/settings/jira-settings-page').then((m) => m.JiraSettingsPage),
  },
  {
    path: 'present/:id',
    title: 'Present · Squad Status Board',
    canActivate: [authGuard],
    loadComponent: () => import('./features/present/present-page').then((m) => m.PresentPage),
  },
  {
    path: 'present',
    title: 'Present · Squad Status Board',
    canActivate: [authGuard],
    loadComponent: () => import('./features/present/present-page').then((m) => m.PresentPage),
  },
  {
    path: 'slide/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/slide/slide-page').then((m) => m.SlidePage),
  },
  {
    path: '**',
    loadComponent: () => import('./features/not-found/not-found-page').then((m) => m.NotFoundPage),
  },
];
