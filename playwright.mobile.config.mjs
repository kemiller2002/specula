import { defineConfig, devices } from "@playwright/test";

// Focused mobile-WebKit coverage for controls that use native iPhone behavior.
// The full deterministic acceptance matrix remains in the Chromium project.
export default defineConfig({
  testDir: "tests/browser",
  testMatch: /mobile\.webkit\.spec\.mjs/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 60_000,
  reporter: [["list"], ["json", { outputFile: "test-results/mobile-webkit-results.json" }]],
  use: {
    ...devices["iPhone 13"],
    baseURL: "http://127.0.0.1:4173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "mobile-webkit", use: { browserName: "webkit" } }],
  webServer: {
    command: "node scripts/serve.mjs dist 4173",
    url: "http://127.0.0.1:4173/",
    reuseExistingServer: !process.env.CI,
  },
});
