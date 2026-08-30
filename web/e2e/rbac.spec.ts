import { expect, test } from '@playwright/test';
import { ACCOUNTS, DEMO_BOARD_ID, signIn } from './helpers';

/**
 * Spec section 13: "RBAC enforced server-side; viewers cannot write."
 *
 * The UI assertions confirm the affordances are hidden; the direct API call confirms
 * the server refuses the request anyway, which is the part that actually matters.
 */
test.describe('access control', () => {
  test('an unauthenticated visitor is sent to the login page', async ({ page }) => {
    await page.goto('/portfolio');
    await expect(page).toHaveURL(/[/]login/, { timeout: 20_000 });
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  });

  test('a viewer gets a read-only board with no write controls', async ({ page }) => {
    await signIn(page, ACCOUNTS.viewer);
    await page.goto(`/boards/${DEMO_BOARD_ID}`);

    await expect(page.locator('.builder__readonly')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: '+ Add to squad' })).toHaveCount(0);

    // But they can still do their job.
    await expect(page.getByRole('button', { name: 'Present' })).toBeVisible();
    await expect(page.locator('.slide__title')).toHaveText('OPD Screen Revamp');
  });

  test('the server refuses a viewer write even when the UI is bypassed', async ({ page }) => {
    await signIn(page, ACCOUNTS.viewer);

    // Replay the request the hidden Save button would have made.
    const status = await page.evaluate(async (boardId) => {
      const token = localStorage.getItem('ssb.access');
      const response = await fetch(`/api/v1/boards/${boardId}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          id: boardId,
          title: 'Viewer should not be able to do this',
          product: 'VIDA HIS',
          squadName: 'Squad Alpha',
          sprint: 'Sprint 14',
          status: 0,
          progressPercent: 1,
        }),
      });
      return response.status;
    }, DEMO_BOARD_ID);

    expect(status).toBe(403);
  });

  test('the roster is admin-only, in the nav and on the route', async ({ page }) => {
    await signIn(page, ACCOUNTS.productOwner);
    await expect(page.getByRole('link', { name: 'Roster' })).toHaveCount(0);

    // Going straight to the URL is bounced by the guard.
    await page.goto('/roster');
    await expect(page).toHaveURL(/[/]portfolio/, { timeout: 20_000 });
  });

  test('an admin sees the roster and can open it', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);
    await page.getByRole('link', { name: 'Roster' }).click();
    await expect(page).toHaveURL(/[/]roster/, { timeout: 20_000 });

    await expect(page.locator('.table tbody tr').first()).toBeVisible();
  });

  test('signing out clears the session', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);
    await page.getByRole('button', { name: 'Sign out' }).click();
    await expect(page).toHaveURL(/[/]login/, { timeout: 20_000 });

    // And the protected route stays protected afterwards.
    await page.goto('/portfolio');
    await expect(page).toHaveURL(/[/]login/, { timeout: 20_000 });
  });
});
