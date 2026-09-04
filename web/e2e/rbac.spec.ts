import { expect, test, type Page } from '@playwright/test';
import { ACCOUNTS, DEMO_PASSWORD, signIn } from './helpers';

/**
 * Spec section 13: "RBAC enforced server-side; viewers cannot write."
 *
 * A clean install has only the administrator, so the Product Owner and Viewer cases
 * are skipped unless those accounts exist — run the API with
 * <c>Database__SeedDemoData=true</c> to exercise them. They are skipped rather than
 * deleted because they assert the rule that matters most, and a green suite that
 * silently stopped checking it would be worse than an obviously skipped one.
 */
test.describe('access control', () => {
  test('an unauthenticated visitor is sent to the login page', async ({ page }) => {
    await page.goto('/portfolio');
    await expect(page).toHaveURL(/[/]login/, { timeout: 20_000 });
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  });

  test('an admin sees the roster and can open it', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);
    await page.getByRole('link', { name: 'Roster' }).click();
    await expect(page).toHaveURL(/[/]roster/, { timeout: 20_000 });
  });

  test('signing out clears the session', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);
    await page.getByRole('button', { name: 'Sign out' }).click();
    await expect(page).toHaveURL(/[/]login/, { timeout: 20_000 });

    // And the protected route stays protected afterwards.
    await page.goto('/portfolio');
    await expect(page).toHaveURL(/[/]login/, { timeout: 20_000 });
  });

  test('the server refuses a viewer write even when the UI is bypassed', async ({ page }) => {
    test.skip(!(await accountExists(page, ACCOUNTS.viewer)), 'viewer account not seeded');

    await signIn(page, ACCOUNTS.viewer);

    // A viewer needs a board to attempt to write to; make one as admin first would
    // need a second session, so instead assert the API refuses a fabricated write.
    const status = await page.evaluate(async () => {
      const token = localStorage.getItem('ssb.access');
      const response = await fetch('/api/v1/boards', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          title: 'Viewer should not be able to do this',
          product: 'VIDA HIS',
          squadName: 'Nope',
          sprint: null,
          status: 0,
          progressPercent: 1,
        }),
      });
      return response.status;
    });

    expect(status).toBe(403);
  });

  test('the roster is admin-only, in the nav and on the route', async ({ page }) => {
    test.skip(
      !(await accountExists(page, ACCOUNTS.productOwner)),
      'product owner account not seeded',
    );

    await signIn(page, ACCOUNTS.productOwner);
    await expect(page.getByRole('link', { name: 'Roster' })).toHaveCount(0);

    // Going straight to the URL is bounced by the guard.
    await page.goto('/roster');
    await expect(page).toHaveURL(/[/]portfolio/, { timeout: 20_000 });
  });
});

/** Probes the login endpoint so optional-account tests can skip instead of failing. */
async function accountExists(page: Page, email: string): Promise<boolean> {
  await page.goto('/login');

  return page.evaluate(
    async ([address, password]) => {
      const response = await fetch('/api/v1/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: address, password }),
      });
      return response.ok;
    },
    [email, DEMO_PASSWORD],
  );
}
