import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ProgressRing } from './progress-ring';
import { CompositionBar } from './composition-bar';
import { StatusBadge } from './status-badge';
import { SlideCanvas } from './slide-canvas';
import type { BoardDetail, Composition } from '../../core/models/board.models';

const composition: Composition = {
  total: 4,
  legendText: '1 Product Owner · 2 Developers · 1 QA Engineer',
  segments: [
    { role: 0, label: 'Product Owner', color: '#2DD4BF', count: 1, percent: 25 },
    { role: 2, label: 'Developer', color: '#6366F1', count: 2, percent: 50 },
    { role: 3, label: 'QA Engineer', color: '#F59E0B', count: 1, percent: 25 },
  ],
};

const board: BoardDetail = {
  id: 'b1',
  title: 'OPD Screen Revamp',
  product: 'VIDA HIS',
  squadName: 'Squad Alpha',
  sprint: 'Sprint 14',
  status: 0,
  statusLabel: 'On Track',
  statusColor: '#34D399',
  progressPercent: 68,
  orderIndex: 0,
  updatedAt: '2026-08-30T00:00:00Z',
  blockerNote: null,
  velocity: null,
  targetDate: null,
  jiraProjectKey: null,
  jiraBoardId: null,
  createdBy: 'seed',
  createdAt: '2026-08-30T00:00:00Z',
  composition,
  warnings: [],
  members: [
    {
      id: 'm1',
      personId: 'p1',
      fullName: 'Nadia Al-Harbi',
      initials: 'NA',
      role: 0,
      roleLabel: 'Product Owner',
      roleColor: '#2DD4BF',
      detail: 'Outpatient journey',
      allocationPercent: null,
      orderIndex: 0,
    },
    {
      id: 'm2',
      personId: 'p2',
      fullName: 'Huda Rahman',
      initials: 'HR',
      role: 2,
      roleLabel: 'Developer',
      roleColor: '#6366F1',
      detail: 'Angular · Signals',
      allocationPercent: 50,
      orderIndex: 1,
    },
  ],
};

@Component({
  imports: [ProgressRing],
  template: `<app-progress-ring [percent]="percent()" />`,
})
class RingHost {
  readonly percent = signal(68);
}

@Component({
  imports: [SlideCanvas],
  template: `<app-slide-canvas [board]="board()" />`,
})
class CanvasHost {
  readonly board = signal<BoardDetail>(board);
}

describe('ProgressRing', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    }),
  );

  it('renders the percentage as readable text, not just a shape', async () => {
    const fixture = TestBed.createComponent(RingHost);
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.ring__value')?.textContent).toContain('68');
    expect(el.querySelector('.ring')?.getAttribute('aria-label')).toBe(
      'Progress: 68 percent complete',
    );
  });

  it('converts the percentage to a conic sweep', async () => {
    const fixture = TestBed.createComponent(RingHost);
    await fixture.whenStable();

    const ring = (fixture.nativeElement as HTMLElement).querySelector('.ring') as HTMLElement;
    // 68% of a full turn.
    expect(ring.style.getPropertyValue('--ring-sweep')).toBe('244.8deg');
  });

  it('clamps out-of-range values rather than overfilling the ring', async () => {
    const fixture = TestBed.createComponent(RingHost);
    fixture.componentInstance.percent.set(150);
    await fixture.whenStable();

    const ring = (fixture.nativeElement as HTMLElement).querySelector('.ring') as HTMLElement;
    expect(ring.style.getPropertyValue('--ring-sweep')).toBe('360deg');

    fixture.componentInstance.percent.set(-20);
    await fixture.whenStable();
    expect(ring.style.getPropertyValue('--ring-sweep')).toBe('0deg');
  });
});

describe('CompositionBar', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    }),
  );

  it('renders one segment per role, sized by percent', async () => {
    const fixture = TestBed.createComponent(CompositionBar);
    fixture.componentRef.setInput('composition', composition);
    await fixture.whenStable();

    const segments = (fixture.nativeElement as HTMLElement).querySelectorAll('.comp__segment');
    expect(segments.length).toBe(3);
    expect((segments[1] as HTMLElement).style.width).toBe('50%');
  });

  it('states the counts as text so the meaning is never colour-only', async () => {
    const fixture = TestBed.createComponent(CompositionBar);
    fixture.componentRef.setInput('composition', composition);
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Product Owner');
    expect(text).toContain('Developer');
    expect(text).toContain('QA Engineer');
  });

  it('shows an empty state for a squad with nobody on it', async () => {
    const fixture = TestBed.createComponent(CompositionBar);
    fixture.componentRef.setInput('composition', {
      total: 0,
      legendText: 'No members yet',
      segments: [],
    } satisfies Composition);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No members yet');
  });
});

describe('StatusBadge', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    }),
  );

  it('renders the label and applies the supplied colour token', async () => {
    const fixture = TestBed.createComponent(StatusBadge);
    fixture.componentRef.setInput('label', 'Blocked');
    fixture.componentRef.setInput('color', '#F87171');
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.badge__label')?.textContent?.trim()).toBe('Blocked');
    expect(
      (el.querySelector('.badge') as HTMLElement).style.getPropertyValue('--badge-color'),
    ).toBe('#F87171');
  });
});

describe('SlideCanvas', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    }),
  );

  it('renders the eyebrow, title and squad line', async () => {
    const fixture = TestBed.createComponent(CanvasHost);
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.slide__product')?.textContent).toContain('VIDA HIS');
    expect(el.querySelector('.slide__sprint')?.textContent).toContain('Sprint 14');
    expect(el.querySelector('.slide__title')?.textContent).toContain('OPD Screen Revamp');
    expect(el.querySelector('.slide__squad')?.textContent).toContain('Squad Alpha');
    expect(el.querySelector('.slide__squad')?.textContent).toContain('2 people');
  });

  it('renders a card for every squad member with their role colour', async () => {
    const fixture = TestBed.createComponent(CanvasHost);
    await fixture.whenStable();

    const cards = (fixture.nativeElement as HTMLElement).querySelectorAll('.member');
    expect(cards.length).toBe(2);
    expect((cards[0] as HTMLElement).style.getPropertyValue('--member-color')).toBe('#2DD4BF');
    expect(cards[0].textContent).toContain('Nadia Al-Harbi');
    expect(cards[1].textContent).toContain('50%');
  });

  it('shows the blocker note in the footer only when there is one', async () => {
    const fixture = TestBed.createComponent(CanvasHost);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('.slide__blocker')).toBeNull();

    fixture.componentInstance.board.set({ ...board, blockerNote: 'Waiting on sign-off' });
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.slide__blocker')?.textContent,
    ).toContain('Waiting on sign-off');
  });

  it('falls back to an empty state when the squad has no members', async () => {
    const fixture = TestBed.createComponent(CanvasHost);
    fixture.componentInstance.board.set({ ...board, members: [] });
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('.slide__empty')).not.toBeNull();
  });
});
