import { defineConfig, devices } from "@playwright/test";

// Live checks against real public GitHub data. SPECULA_URL selects the
// deployed site; without it the local dist/ build is served.
const url = process.env.SPECULA_URL;
export default defineConfig({
  testDir: "tests/browser",
  testMatch: /live\..*\.spec\.mjs/,
  workers: 1,
  retries: 1,
  timeout: 120_000,
  reporter: [["list"], ["json", { outputFile: "test-results/live-results.json" }]],
  use: {
    baseURL: url ?? "http://127.0.0.1:4173",
    trace: "retain-on-failure",
    screenshot: "on",
    // Only for sandboxes whose outbound HTTPS goes through an intercepting proxy.
    ...(process.env.PLAYWRIGHT_PROXY ? { proxy: { server: process.env.PLAYWRIGHT_PROXY, bypass: "127.0.0.1,localhost" }, ignoreHTTPSErrors: true } : {}),
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: url ? undefined : { command: "node scripts/serve.mjs dist 4173", url: "http://127.0.0.1:4173/", reuseExistingServer: true },
});
