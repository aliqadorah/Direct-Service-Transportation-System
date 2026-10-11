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
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
});
