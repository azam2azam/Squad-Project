import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideZonelessChangeDetection } from '@angular/core';
import { App } from './app';
import { MetadataService } from './core/services/metadata.service';
import type { Metadata, RoleOption } from './core/models/board.models';

/** Minimal stand-in so the shell can be tested without an HTTP round trip. */
class MetadataServiceStub {
  private readonly loaded: Metadata = {
    roles: [
      { value: 0, name: 'ProductOwner', label: 'Product Owner', color: '#2DD4BF' },
    ] as RoleOption[],
    statuses: [],
  };

  readonly roles = () => this.loaded.roles;
  readonly statuses = () => this.loaded.statuses;
  readonly jiraSyncEnabled = () => false;
  readonly serverExportEnabled = () => false;
  readonly isLoaded = () => true;
  async load(): Promise<void> {}
}

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: MetadataService, useClass: MetadataServiceStub },
      ],
    }).compileComponents();
  });

  it('creates the shell', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('shows the product name and primary navigation', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.shell-header__title')?.textContent).toContain(
      'Squad Status Board',
    );
    expect(compiled.querySelectorAll('.shell-nav__link').length).toBe(2);
  });

  it('reports the API as connected once reference data is present', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.shell-chip--ok')?.textContent).toContain('API connected');
  });
});
