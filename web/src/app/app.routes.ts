import { Routes } from '@angular/router';

/**
 * Feature routes are lazy so Present mode and the export-only slide route load in
 * isolation without pulling in the builder.
 *
 * `/slide/:id` and `/slide/all` deliberately render outside the app shell — they are
 * what the headless export renderer loads.
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
    path: 'present/:id',
    title: 'Present · Squad Status Board',
    loadComponent: () => import('./features/present/present-page').then((m) => m.PresentPage),
  },
  {
    path: 'present',
    title: 'Present · Squad Status Board',
    loadComponent: () => import('./features/present/present-page').then((m) => m.PresentPage),
  },
  {
    path: 'slide/:id',
    loadComponent: () => import('./features/slide/slide-page').then((m) => m.SlidePage),
  },
  {
    path: '**',
    loadComponent: () => import('./features/not-found/not-found-page').then((m) => m.NotFoundPage),
  },
];
