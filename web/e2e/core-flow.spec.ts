import { expect, test } from '@playwright/test';
import { ACCOUNTS, DEMO_BOARD_ID, signIn } from './helpers';

/**
 * The spec's required end-to-end path (section 13): create a board, add members,
 * present, export.
 */
test.describe('core flow', () => {
  test('create a board, add a member, present it, export a PNG', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);

    // ---- create ----
    await page.getByRole('button', { name: 'New board' }).click();
    await expect(page).toHaveURL(/[/]boards[/]/, { timeout: 20_000 });

    const title = `E2E ${Date.now()}`;
    await page.getByPlaceholder('OPD Screen Revamp').fill(title);
    await page.getByPlaceholder('Squad Alpha').fill('Squad E2E');

    // The slide reflects the edit before anything is saved.
    await expect(page.locator('.slide__title')).toHaveText(title);

    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();

    // ---- add a member from the roster ----
    await page.locator('#memberName').fill('Tariq');
    await page.locator('.typeahead__option').first().click();
    await page.getByRole('button', { name: '+ Add to squad' }).click();

    await expect(page.locator('.member')).toHaveCount(1);
    await expect(page.locator('.comp__n')).toHaveText('1 person');

    // ---- export a PNG ----
    const download = page.waitForEvent('download', { timeout: 30_000 });
    await page.getByRole('button', { name: 'PNG', exact: true }).click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(/\.png$/);

    // ---- present ----
    await page.getByRole('button', { name: 'Present' }).click();
    await expect(page).toHaveURL(/[/]present[/]/, { timeout: 20_000 });

    await expect(page.locator('.present__stage .slide')).toBeVisible();
    // The app shell must not appear in Present mode.
    await expect(page.locator('.shell-header')).toHaveCount(0);

    // Esc returns to the editor.
    await page.keyboard.press('Escape');
    await expect(page).toHaveURL(/[/]boards[/]/, { timeout: 20_000 });

    // ---- clean up so reruns start from the same state ----
    await page.goto('/portfolio');
    const card = page.locator('.board', { hasText: title });
    await card.hover();
    await card.getByRole('button', { name: `Delete ${title}` }).click();
    await expect(page.locator('.board', { hasText: title })).toHaveCount(0);
  });

  test('the seeded demo board renders the full slide', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);
    await page.goto(`/boards/${DEMO_BOARD_ID}`);

    await expect(page.locator('.slide__title')).toHaveText('OPD Screen Revamp');
    await expect(page.locator('.slide__tag')).toHaveText('VIDA HIS');
    await expect(page.locator('.slide__squad')).toContainText('Squad Alpha');
    await expect(page.locator('.ring__pct')).toHaveText('68%');
    await expect(page.locator('.member')).toHaveCount(6);

    // Correct plural — the prototype's naive +"s" would render "DevOpss".
    await expect(page.locator('.comp__lg')).toContainText([
      '1 Product Owner',
      '1 Tech Lead',
      '2 Developers',
      '1 QA Engineer',
      '1 UI/UX Designer',
    ]);
  });

  test('the standalone slide route renders without app chrome', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);
    await page.goto(`/slide/${DEMO_BOARD_ID}`);

    await expect(page.locator('.slide__title')).toHaveText('OPD Screen Revamp');
    // This is what the headless export renderer captures.
    await expect(page.locator('.shell-header')).toHaveCount(0);
  });
});
