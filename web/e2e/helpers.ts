import { expect, type Page } from '@playwright/test';

export const DEMO_PASSWORD = 'Demo!Pass123';

export const ACCOUNTS = {
  admin: 'admin@pirt.example',
  productOwner: 'po@pirt.example',
  viewer: 'viewer@pirt.example',
} as const;

/**
 * Signs in through the real login form rather than injecting a token, so the test
 * covers the same path a person takes.
 */
export async function signIn(page: Page, email: string): Promise<void> {
  await page.goto('/login');

  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(DEMO_PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();

  // toHaveURL polls rather than waiting for a load event: an Angular route change is
  // a pushState, which never fires one.
  await expect(page).toHaveURL(/[/]portfolio/, { timeout: 20_000 });
}
