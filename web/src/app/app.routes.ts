import { Routes } from '@angular/router';

/**
 * Feature routes are lazy so Present mode and the export-only slide route can be
 * loaded in isolation by the headless renderer without pulling in the builder.
 */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'portfolio',
  },
  {
    path: 'portfolio',
    title: 'Portfolio · Squad Status Board',
    loadComponent: () => import('./features/portfolio/portfolio-page').then((m) => m.PortfolioPage),
  },
  {
    path: 'boards/:id',
    title: 'Board editor · Squad Status Board',
    loadComponent: () =>
      import('./features/boards/board-editor-page').then((m) => m.BoardEditorPage),
  },
  {
    path: 'roster',
    title: 'Roster · Squad Status Board',
    loadComponent: () => import('./features/roster/roster-page').then((m) => m.RosterPage),
  },
  {
    path: '**',
    loadComponent: () => import('./features/not-found/not-found-page').then((m) => m.NotFoundPage),
  },
];
