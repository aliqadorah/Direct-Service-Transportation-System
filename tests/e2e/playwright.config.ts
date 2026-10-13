import { defineConfig } from "@playwright/test";
import { BASE_URL } from "./support/env";

/**
 * E2E tests run against a real host on its own port and database (DirectServiceMaster_E2E, recreated by global-setup), in the installed
 * Microsoft Edge (the same browser the PDF engine uses), one worker at a time so flows that share a tenant do not race.
 */
export default defineConfig({
  testDir: "./specs",
  globalSetup: "./support/global-setup.ts",
  globalTeardown: "./support/global-teardown.ts",
  timeout: 120_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"], ["html", { open: "never", outputFolder: "../../TestResults/e2e-report" }]],
  outputDir: "../../TestResults/e2e-artifacts",
  use: {
    baseURL: BASE_URL,
    channel: "msedge",
    ignoreHTTPSErrors: true,
    viewport: { width: 1366, height: 860 },
    // No click or page load may wait without end: a missing element fails its step (owner, 13 Oct 2026: a visual run hung 52 minutes).
    actionTimeout: 30_000,
    navigationTimeout: 60_000,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
});
