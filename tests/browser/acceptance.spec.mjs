import { test, expect } from "@playwright/test";
import { install, scenarios, choose, ready, settled, commit, HEAD, OLD } from "./github-fixtures.mjs";

const nav = (page, name) => page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name, exact: true }).click();

const status = (page) => page.locator(".app-status .ef-status-lozenge");
const headChecks = (page) => page.locator("#ov-head-title").locator("..").locator(".app-strip").first();

test("first selection, reload restore and navigation keep the repository", async ({ page }) => {
  await install(page, scenarios.passing);
  await choose(page, "https://github.com/octo/demo");
  await settled(page);
  await expect(page.locator(".app-repo__name")).toHaveText("octo/demo");
  await expect(status(page)).toHaveText("Fresh");
  await expect(headChecks(page)).toContainText("Passing");

  for (const [link, title] of [["Activity", "Activity"], ["Verify", "Verification"], ["Replay", "Replay"], ["Overview", "Overview"]]) {
    await page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: link }).click();
    await expect(page.locator("h1")).toHaveText(title);
    await expect(page.locator(".app-repo__name")).toHaveText("octo/demo");
  }

  await page.reload();
  await ready(page);
  await expect(page.locator("h1")).toHaveText("Overview");
  await expect(page.locator(".app-repo__name")).toHaveText("octo/demo");
  await settled(page);
  await expect(status(page)).toHaveText("Fresh");
  const stored = await page.evaluate(() => JSON.parse(localStorage.getItem("specula.preferences")));
  expect(stored.version).toBe(1);
  expect(stored.selectedRepository).toBe("octo/demo");
  expect(JSON.stringify(stored)).not.toMatch(/commit|sha|token/i);
});

test("a new browser session restores the repository and fetches fresh data", async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();
  await install(page, scenarios.passing);
  await choose(page, "octo/demo");
  const state = await context.storageState();
  await context.close();
  const reopened = await browser.newContext({ storageState: state });
  const page2 = await reopened.newPage();
  const log = [];
  await install(page2, scenarios.passing, { log });
  await page2.goto("/");
  await ready(page2);
  await expect(page2.locator(".app-repo__name")).toHaveText("octo/demo");
  await settled(page2);
  expect(log.some((u) => u.endsWith("/repos/octo/demo"))).toBe(true);
  await reopened.close();
});

test("repeated refresh keeps selection, does not duplicate events and never runs concurrently", async ({ page }) => {
  const log = [];
  await install(page, scenarios.passing, { log, delay: () => 30 });
  await choose(page, "octo/demo");
  await settled(page);
  const button = page.getByRole("button", { name: /^Refresh/ });
  for (let i = 0; i < 10; i++) {
    await button.click({ trial: false }).catch(() => {});
    await settled(page);
  }
  const repoCalls = log.filter((u) => u.endsWith("/repos/octo/demo")).length;
  expect(repoCalls).toBeLessThanOrEqual(11);
  await nav(page, "Activity");
  const ids = await page.locator(".app-feed .app-event:visible").evaluateAll((els) => els.length);
  expect(ids).toBe(6); // 2 commits, 1 PR opened, 2 workflow runs, 1 HEAD check: no duplicates
});

test("switching repository during an in-flight check or fetch never shows the previous repository", async ({ page }) => {
  const scenario = () => ({
    ...scenarios.passing(),
    commitsByRepo: {
      "slow/alpha": [commit(HEAD, "ALPHA-ONLY commit", 60)],
      "octo/beta": [commit(HEAD, "beta commit", 60)],
      "fast/gamma": [commit(HEAD, "GAMMA-ONLY commit", 60)],
    },
  });
  await install(page, scenario, { delay: (u) => (u.includes("/repos/slow/alpha") ? 1500 : u.includes("/repos/fast/gamma/commits") ? 1500 : 0) });
  await page.goto("/");
  await ready(page);
  // 1. Switch while the first repository is still being checked.
  await page.getByLabel("GitHub repository").fill("slow/alpha");
  await page.getByRole("button", { name: "Observe repository" }).click();
  await page.getByLabel("GitHub repository").fill("octo/beta");
  await page.getByRole("button", { name: /Observe repository|Checking/ }).click();
  await page.locator("h1", { hasText: "Overview" }).waitFor();
  await settled(page);
  await page.waitForTimeout(2000);
  await expect(page.locator(".app-repo__name")).toHaveText("octo/beta");
  await expect(page.locator("body")).not.toContainText("ALPHA-ONLY");
  // 2. Select gamma, then switch back to beta while gamma's commits are in flight.
  await page.goto("/#/select");
  await page.getByLabel("GitHub repository").fill("fast/gamma");
  await page.getByRole("button", { name: "Observe repository" }).click();
  await expect(page.locator(".app-repo__name")).toHaveText("fast/gamma");
  await page.goto("/#/select");
  await page.getByLabel("GitHub repository").fill("octo/beta");
  await page.getByRole("button", { name: "Observe repository" }).click();
  await expect(page.locator(".app-repo__name")).toHaveText("octo/beta");
  await settled(page);
  await page.waitForTimeout(2000);
  await expect(page.locator("body")).not.toContainText("GAMMA-ONLY");
  await expect(page.locator("body")).toContainText("beta commit");
});

test("failed CI on HEAD is shown as failing and placed under attention", async ({ page }) => {
  await install(page, scenarios.failedHead);
  await choose(page, "octo/demo");
  await settled(page);
  await expect(headChecks(page)).toContainText("Failing");
  await expect(page.locator("#ov-attention-title").locator("..")).toContainText("Failing on HEAD");
  await nav(page, "Verify");
  await expect(page.locator("#v-blockers-title").locator("..")).toContainText("build");
});

test("an old successful run does not verify a new unverified HEAD", async ({ page }) => {
  await install(page, scenarios.oldGreenNewUnverified);
  await choose(page, "octo/demo");
  await settled(page);
  await expect(headChecks(page)).toContainText("Unknown");
  await expect(headChecks(page)).toContainText(`older commit ${OLD.slice(0, 7)}`);
  await expect(headChecks(page)).not.toContainText("Passing");
});

test("repository without Actions shows unknown CI and unavailable structured verification", async ({ page }) => {
  await install(page, scenarios.noActions);
  await choose(page, "octo/demo");
  await settled(page);
  await expect(headChecks(page)).toContainText("Unknown");
  await nav(page, "Verify");
  await expect(page.locator("#v-req-title").locator("..")).toContainText("Structured verification unavailable");
  await expect(page.locator("#v-checks-title").locator("..")).toContainText("No check runs or workflow runs were found");
});

test("deployment without smoke-test evidence is not reported as a working application", async ({ page }) => {
  await install(page, scenarios.deploymentNoSmoke);
  await choose(page, "octo/demo");
  await settled(page);
  await nav(page, "Verify");
  const truth = page.locator("#v-truth-title").locator("..");
  await expect(truth).toContainText("Deployment URL available");
  await expect(truth).toContainText("Recorded");
  await expect(truth.locator(".app-row", { hasText: "Application smoke test" })).toContainText("Not verified");
  await expect(truth.locator(".app-row", { hasText: "Application smoke test" }).locator(".ef-status-lozenge")).not.toHaveAttribute("data-state", "ok");
});

test("unsupported evidence schema is explained and GitHub facts remain", async ({ page }) => {
  await install(page, scenarios.unsupportedEvidence);
  await choose(page, "octo/demo");
  await settled(page);
  await nav(page, "Verify");
  await expect(page.locator("#v-req-title").locator("..")).toContainText("unsupported schema (causa.traceability/9)");
  await expect(page.locator("#v-src-title").locator("..")).toContainText("Unsupported schema");
  await expect(page.locator("#v-checks-title").locator("..")).toContainText("build");
});

test("supported evidence lists requirements, blockers and decisions with sources", async ({ page }) => {
  await install(page, scenarios.supportedEvidence);
  await choose(page, "octo/demo");
  await settled(page);
  await nav(page, "Verify");
  const req = page.locator("#v-req-title").locator("..");
  await expect(req).toContainText("2 requirements");
  await expect(page.locator("#v-blockers-title").locator("..")).toContainText("UI-002");
  await page.locator("summary", { hasText: "Recorded decisions" }).click();
  await expect(page.locator("#v-dec-title").locator("..")).toContainText("KD-001");
});

test("offline refresh keeps the selection and marks data stale", async ({ page, context }) => {
  let offline = false;
  await install(page, () => ({ ...scenarios.passing(), offline }));
  await choose(page, "octo/demo");
  await settled(page);
  offline = true;
  await context.setOffline(true);
  await page.getByRole("button", { name: /^Refresh/ }).click();
  await expect(status(page)).toHaveText("Stale");
  await expect(page.locator(".app-repo__name")).toHaveText("octo/demo");
  await expect(page.locator(".app-status")).toContainText("not current");
  await context.setOffline(false);
});

test("rate limiting is reported without losing the selection", async ({ page }) => {
  let limited = false;
  await install(page, () => ({ ...scenarios.passing(), rateLimited: limited }));
  await choose(page, "octo/demo");
  await settled(page);
  limited = true;
  await page.getByRole("button", { name: /^Refresh/ }).click();
  await expect(status(page)).toHaveText("Rate limited");
  await expect(page.locator(".app-repo__name")).toHaveText("octo/demo");
  await page.getByRole("button", { name: /^Refresh/ }).click();
  await expect(page.locator(".app-notice")).toContainText("rate limit");
});

test("malformed and missing repositories give useful errors and keep the saved one", async ({ page }) => {
  await install(page, () => ({ ...scenarios.passing(), missing: ["octo/ghost"] }));
  await choose(page, "octo/demo");
  await page.goto("/#/select");
  await page.getByLabel("GitHub repository").fill("javascript:alert(1)");
  await page.getByRole("button", { name: "Observe repository" }).click();
  await expect(page.getByRole("alert")).toContainText("Only https://github.com URLs");
  await page.getByLabel("GitHub repository").fill("octo/ghost");
  await page.getByRole("button", { name: "Observe repository" }).click();
  await expect(page.getByRole("alert")).toContainText("was not found");
  await page.reload();
  await ready(page);
  await page.goto("/#/overview");
  await expect(page.locator(".app-repo__name")).toHaveText("octo/demo");
});

test("corrupt localStorage falls back safely", async ({ page }) => {
  await install(page, scenarios.passing);
  await page.addInitScript(() => localStorage.setItem("specula.preferences", "{broken"));
  await page.goto("/");
  await ready(page);
  await expect(page.locator("h1")).toHaveText("Choose a repository");
  await expect(page.locator(".app-notice")).toContainText("corrupted");
});

test("blocked localStorage keeps the app usable for the session", async ({ page }) => {
  await install(page, scenarios.passing);
  await page.addInitScript(() => {
    const deny = () => { throw new DOMException("blocked", "SecurityError"); };
    Object.defineProperty(window, "localStorage", { get: deny, configurable: true });
  });
  await choose(page, "octo/demo");
  await expect(page.locator(".app-repo__name")).toHaveText("octo/demo");
});

test("forget repository returns to the picker and clears the saved selection", async ({ page }) => {
  await install(page, scenarios.passing);
  await choose(page, "octo/demo");
  await page.goto("/#/settings");
  await page.getByRole("button", { name: "Forget repository" }).click();
  await expect(page.locator("h1")).toHaveText("Choose a repository");
  const stored = await page.evaluate(() => JSON.parse(localStorage.getItem("specula.preferences")));
  expect(stored.selectedRepository).toBeNull();
});

test("replay shows a configured window, planned versus observed milestones and presentation view", async ({ page }) => {
  await install(page, scenarios.supportedEvidence);
  await choose(page, "octo/demo");
  await settled(page);
  await page.goto("/#/settings");
  await page.getByRole("button", { name: "Use the recorded kickoff" }).click();
  await expect(page.getByText("Kickoff set from evidence/kickoff/kickoff.json")).toBeVisible();
  await page.goto("/#/replay");
  await page.getByRole("button", { name: "Use the configured competition window" }).click();
  await expect(page.locator("#r-events-title").locator("..")).toContainText("events in the window");
  const ms = page.locator("#r-ms-title").locator("..");
  await expect(ms).toContainText("First passing CI");
  await expect(ms).toContainText("Planned");
  await expect(ms).toContainText("Observed");
  await expect(ms.locator(".app-milestone", { hasText: "Acceptance verdict" })).toContainText("Not observed");
  await page.getByRole("button", { name: "Presentation view" }).click();
  await expect(page.locator(".app-presentation")).toBeVisible();
  await expect(page.locator(".app-presentation")).toContainText("Final verification");
});

test("activity filters and event details keep timestamps, SHAs and source links", async ({ page }) => {
  await install(page, scenarios.passing);
  await choose(page, "octo/demo");
  await settled(page);
  await nav(page, "Activity");
  await page.locator("section[aria-label=Activity]").getByText(/^Commits \(/).click();
  const items = page.locator(".app-feed .app-event:visible");
  await expect(items).toHaveCount(2);
  await expect(items.first()).toContainText(HEAD.slice(0, 7));
  const link = items.first().getByRole("link", { name: /View commit on GitHub/ });
  await expect(link).toHaveAttribute("href", `https://github.com/octo/demo/commit/${HEAD}`);
  await items.first().getByRole("button", { name: "Details" }).click();
  await expect(items.first()).toContainText("commit:" + HEAD);
  await page.getByRole("button", { name: /^Refresh/ }).click();
  await settled(page);
  await expect(page.locator('input[name="activity-filter"]:checked').locator("..")).toContainText("Commits");
});
