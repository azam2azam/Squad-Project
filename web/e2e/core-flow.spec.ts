import { expect, test } from '@playwright/test';
import { ACCOUNTS, signIn } from './helpers';

/**
 * The spec's required end-to-end path (section 13): create a board, add members,
 * present, export.
 *
 * These create everything they need and remove it afterwards. Nothing depends on seeded
 * demo content, because a real deployment starts empty.
 */
test.describe('core flow', () => {
  test('create a board, add a member, present it, export a PNG', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);

    await page.getByRole('button', { name: 'New board' }).click();
    await expect(page).toHaveURL(/[/]boards[/]/, { timeout: 20_000 });

    const title = `E2E ${Date.now()}`;
    await page.getByPlaceholder('OPD Screen Revamp').fill(title);
    await page.getByPlaceholder('Squad Alpha').fill('Squad E2E');

    // The slide reflects the edit before anything is saved.
    await expect(page.locator('.slide__title')).toHaveText(title);

    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();

    // Quick-create a person inline, which also puts them on the roster.
    await page.locator('#memberName').fill('E2E Tester');
    await page.locator('#memberDetail').fill('Automation');
    await page.getByRole('button', { name: '+ Add to squad' }).click();

    await expect(page.locator('.member')).toHaveCount(1);
    await expect(page.locator('.comp__n')).toHaveText('1 person');

    const download = page.waitForEvent('download', { timeout: 30_000 });
    await page.getByRole('button', { name: 'PNG', exact: true }).click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(/\.png$/);

    await page.getByRole('button', { name: 'Present' }).click();
    await expect(page).toHaveURL(/[/]present[/]/, { timeout: 20_000 });
    await expect(page.locator('.present__stage .slide')).toBeVisible();
    // The app shell must not appear in Present mode.
    await expect(page.locator('.shell-header')).toHaveCount(0);

    await page.keyboard.press('Escape');
    await expect(page).toHaveURL(/[/]boards[/]/, { timeout: 20_000 });

    await deleteBoard(page, title);
  });

  test('a board renders the full slide anatomy, and /slide carries no chrome', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);

    await page.getByRole('button', { name: 'New board' }).click();
    await expect(page).toHaveURL(/[/]boards[/]/, { timeout: 20_000 });

    const title = `Slide ${Date.now()}`;
    await page.getByPlaceholder('OPD Screen Revamp').fill(title);
    await page.getByPlaceholder('VIDA HIS').fill('VIDA HIS');
    await page.getByPlaceholder('Squad Alpha').fill('Squad Anatomy');
    await page.getByPlaceholder('Sprint 14').fill('Sprint 3');

    await expect(page.locator('.slide__tag')).toHaveText('VIDA HIS');
    await expect(page.locator('.slide__sprint')).toHaveText('Sprint 3');
    await expect(page.locator('.slide__title')).toHaveText(title);
    await expect(page.locator('.slide__squad')).toContainText('Squad Anatomy');
    await expect(page.locator('.slide__team-h')).toHaveText('The squad');
    await expect(page.locator('.ring__pct')).toBeVisible();

    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();

    // The route the headless export renderer loads.
    const url = page.url();
    const boardId = url.slice(url.lastIndexOf('/') + 1);
    await page.goto(`/slide/${boardId}`);
    await expect(page.locator('.slide__title')).toHaveText(title);
    await expect(page.locator('.shell-header')).toHaveCount(0);

    await deleteBoard(page, title);
  });

  test('a board can be linked to a Jira project', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);

    await page.getByRole('button', { name: 'New board' }).click();
    await expect(page).toHaveURL(/[/]boards[/]/, { timeout: 20_000 });

    const title = `Jira ${Date.now()}`;
    await page.getByPlaceholder('OPD Screen Revamp').fill(title);

    // The link fields are offered whether or not sync is switched on, so a board can
    // be prepared before an admin enables the integration.
    await page.getByPlaceholder('OPD', { exact: true }).fill('TRI');
    await page.getByPlaceholder('42').fill('9');

    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();

    // Survives a reload, i.e. it really persisted.
    await page.reload();
    await expect(page.getByPlaceholder('OPD', { exact: true })).toHaveValue('TRI');
    await expect(page.getByPlaceholder('42')).toHaveValue('9');

    await deleteBoard(page, title);
  });

  test('an empty portfolio reads as clean rather than broken', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);
    await page.goto('/dashboard');

    // Either there is content or there is a clear empty state — never neither.
    const tiles = await page.locator('.tile').count();

    if (tiles === 0) {
      await expect(page.locator('.dash__state')).toContainText('No boards yet');
    } else {
      expect(tiles).toBeGreaterThan(0);
    }
  });
});

/** Removes a board from the portfolio so reruns start from the same state. */
async function deleteBoard(page: import('@playwright/test').Page, title: string): Promise<void> {
  await page.goto('/portfolio');
  const card = page.locator('.board', { hasText: title });
  await card.hover();
  await card.getByRole('button', { name: `Delete ${title}` }).click();
  await expect(page.locator('.board', { hasText: title })).toHaveCount(0);
}
