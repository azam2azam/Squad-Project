import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end configuration.
 *
 * Uses the installed Chrome (`channel: 'chrome'`) rather than a Playwright-managed
 * build. On the development machine the downloaded browsers are blocked from launching
 * by the host's security software — the same problem that stopped PuppeteerSharp's
 * bundled Chromium. CI installs browsers normally, so PLAYWRIGHT_CHANNEL can be unset
 * there to use the pinned build instead.
 *
 * The API and web server must already be running; see e2e/README or:
 *   dotnet run --project src/Api        (port 5220)
 *   npm start --prefix web              (port 4220)
 */
const channel = process.env['PLAYWRIGHT_CHANNEL'] ?? 'chrome';

export default defineConfig({
  testDir: './e2e',
  // The realtime and export specs wait on network round trips, so a flat 5s is tight.
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  // A failing e2e run must not be silently "fixed" by a retry in CI.
  retries: process.env['CI'] ? 1 : 0,
  workers: 1,
  reporter: process.env['CI'] ? [['github'], ['list']] : [['list']],

  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4220',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], channel: channel || undefined },
    },
  ],
});
