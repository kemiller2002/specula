import { defineConfig, devices } from "@playwright/test";

// Deterministic browser acceptance: GitHub is replaced by fixtures
// (tests/browser/github-fixtures.mjs). Live checks use playwright.live.config.mjs.
export default defineConfig({
  testDir: "tests/browser",
  testIgnore: /live\..*/,
  fullyParallel: true,
  workers: process.env.CI ? 2 : 4,
  retries: 0,
  timeout: 60_000,
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-report" }], ["json", { outputFile: "test-results/browser-results.json" }]],
  use: {
    baseURL: "http://127.0.0.1:4173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "node scripts/serve.mjs dist 4173",
    url: "http://127.0.0.1:4173/",
    reuseExistingServer: !process.env.CI,
  },
});
