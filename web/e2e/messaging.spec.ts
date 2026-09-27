import { expect, test } from '@playwright/test';
import { ACCOUNTS, signIn } from './helpers';

/**
 * Team messaging through the browser.
 *
 * The two things worth driving from a real browser rather than a handler test: that a
 * message typed into the composer actually appears in the thread, and that a squad
 * channel really does gather the several boards that squad runs — that second one is the
 * whole reason a squad channel exists separately from a board channel.
 *
 * These create their own conversation and leave it afterwards, so a rerun starts clean.
 */
test.describe('messaging', () => {
  test('open a board channel, post, reply and withdraw', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);

    // Straight from the board, which is where the conversation belongs.
    await page.goto('/portfolio');
    await page.locator('.board-card, .portfolio__row, a[href^="/boards/"]').first().click();
    await expect(page).toHaveURL(/[/]boards[/]/, { timeout: 20_000 });

    await page.getByRole('button', { name: 'Discuss' }).click();
    await expect(page).toHaveURL(/[/]messages[/]/, { timeout: 20_000 });

    const composer = page.getByLabel('Write a message');
    await expect(composer).toBeVisible();

    const body = `Status update ${Date.now()}`;
    await composer.fill(body);
    await page.getByRole('button', { name: 'Send' }).click();

    const bubble = page.locator('.bubble').filter({ hasText: body });
    await expect(bubble).toBeVisible({ timeout: 20_000 });

    // A reply quotes what it answers.
    await bubble.getByRole('button', { name: 'Reply' }).click();
    await expect(page.locator('.composer__ctx')).toContainText('Replying to');

    await composer.fill('Noted.');
    await page.getByRole('button', { name: 'Send' }).click();

    const reply = page.locator('.bubble').filter({ hasText: 'Noted.' });
    await expect(reply).toBeVisible({ timeout: 20_000 });
    await expect(reply.locator('.quote')).toBeVisible();

    // Withdrawal leaves a tombstone rather than erasing the exchange.
    await reply.getByRole('button', { name: 'Delete' }).click();
    await expect(page.locator('.bubble__text--gone')).toBeVisible({ timeout: 20_000 });

    await page.getByRole('button', { name: 'Leave' }).click();
    await expect(page).toHaveURL(/[/]messages$/, { timeout: 20_000 });
  });

  test('a squad channel gathers every board that squad runs', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin);

    await page.goto('/messages');
    await page.getByRole('button', { name: 'New' }).click();

    const sheet = page.locator('.sheet');
    await expect(sheet).toBeVisible();

    // The first squad in the picker. Which one does not matter; that it carries its
    // boards with it does.
    await sheet.locator('.sheet__group', { hasText: 'Squads' }).waitFor();
    await sheet.locator('.target').first().click();

    await expect(page.locator('.thread__title')).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('.tag--squad')).toBeVisible();

    // The point of a squad channel: it spans boards, and says which.
    await expect(page.locator('.thread__boards')).toBeVisible();
    await expect(page.locator('.boardchip').first()).toBeVisible();

    await page.getByRole('button', { name: 'Leave' }).click();
    await expect(page).toHaveURL(/[/]messages$/, { timeout: 20_000 });
  });

  test('the unread badge counts a message somebody else sent', async ({ browser }) => {
    const senderContext = await browser.newContext();
    const readerContext = await browser.newContext();

    try {
      const sender = await senderContext.newPage();
      const reader = await readerContext.newPage();

      await signIn(sender, ACCOUNTS.admin);
      await signIn(reader, ACCOUNTS.productOwner);

      // Both join the same board channel.
      await sender.goto('/portfolio');
      await sender.locator('a[href^="/boards/"]').first().click();
      await expect(sender).toHaveURL(/[/]boards[/]/, { timeout: 20_000 });
      await sender.getByRole('button', { name: 'Discuss' }).click();
      await expect(sender).toHaveURL(/[/]messages[/]/, { timeout: 20_000 });

      const conversationUrl = sender.url();
      await reader.goto(conversationUrl);
      await expect(reader.getByLabel('Write a message')).toBeVisible({ timeout: 20_000 });

      // The reader looks away.
      await reader.goto('/dashboard');

      const body = `Needs your eyes ${Date.now()}`;
      await sender.getByLabel('Write a message').fill(body);
      await sender.getByRole('button', { name: 'Send' }).click();
      await expect(sender.locator('.bubble').filter({ hasText: body })).toBeVisible({
        timeout: 20_000,
      });

      // The badge is polled, so a navigation is what refreshes it.
      await reader.goto('/people');
      await expect(reader.locator('.rail__badge')).toBeVisible({ timeout: 20_000 });

      // Reading it clears the badge again.
      await reader.goto(conversationUrl);
      await expect(reader.locator('.bubble').filter({ hasText: body })).toBeVisible({
        timeout: 20_000,
      });
      await reader.goto('/people');
      await expect(reader.locator('.rail__badge')).toHaveCount(0, { timeout: 20_000 });
    } finally {
      await senderContext.close();
      await readerContext.close();
    }
  });
});
