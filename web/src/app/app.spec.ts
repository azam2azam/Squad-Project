import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { App } from './app';
import { MetadataService } from './core/services/metadata.service';
import { AuthService, type SignedInUser, type UserRoleName } from './core/services/auth.service';
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

/** Signed-out by default; tests call signIn() to change role. */
class AuthServiceStub {
  private readonly current = signal<SignedInUser | null>(null);

  readonly user = this.current.asReadonly();
  readonly isSignedIn = () => this.current() !== null;
  readonly role = () => this.current()?.roleName ?? null;
  readonly canWrite = () => {
    const role = this.role();
    return role === 'Admin' || role === 'ProductOwner';
  };
  readonly isAdmin = () => this.role() === 'Admin';

  signIn(roleName: UserRoleName): void {
    this.current.set({
      id: 'u1',
      email: `${roleName.toLowerCase()}@pirt.example`,
      displayName: 'Nadia Al-Harbi',
      role: 1,
      roleName,
    });
  }

  async restore(): Promise<void> {}
  async logout(): Promise<void> {
    this.current.set(null);
  }
  clear(): void {
    this.current.set(null);
  }
}

function setup() {
  TestBed.configureTestingModule({
    imports: [App],
    providers: [
      provideZonelessChangeDetection(),
      provideRouter([]),
      { provide: MetadataService, useClass: MetadataServiceStub },
      { provide: AuthService, useClass: AuthServiceStub },
    ],
  });

  return TestBed.inject(AuthService) as unknown as AuthServiceStub;
}

describe('App shell', () => {
  it('creates the shell', () => {
    setup();
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('shows the product name', async () => {
    setup();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.shell-header__title')?.textContent).toContain(
      'Squad Status Board',
    );
  });

  it('hides navigation until somebody is signed in', async () => {
    setup();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelectorAll('.shell-nav__link').length).toBe(0);
  });

  it('offers the roster only to an admin', async () => {
    const auth = setup();
    auth.signIn('Admin');

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.shell-nav__link')];
    expect(links.map((l) => l.textContent?.trim())).toEqual(['Overview', 'Boards', 'Roster']);
  });

  it('hides the roster from a product owner', async () => {
    const auth = setup();
    auth.signIn('ProductOwner');

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.shell-nav__link')];
    expect(links.map((l) => l.textContent?.trim())).toEqual(['Overview', 'Boards']);
  });

  it('marks a viewer as read-only in the header', async () => {
    const auth = setup();
    auth.signIn('Viewer');

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const role = (fixture.nativeElement as HTMLElement).querySelector('.shell-user__role');
    expect(role?.textContent?.trim()).toBe('Viewer');
    expect(role?.classList.contains('is-readonly')).toBe(true);
  });
});
