import { test, expect } from "@playwright/test";

// Live integration: real public GitHub data, no fixtures. The observed
// repository is only read. GITHUB_TOKEN (CI) is used solely by this test's
// own Node-side comparison request, never by the application.
const REPO = process.env.SPECULA_LIVE_REPO ?? "kemiller2002/specula";
const EXPECT_SHA = process.env.SPECULA_EXPECT_SHA;
const apiHeaders = process.env.GITHUB_TOKEN ? { authorization: `Bearer ${process.env.GITHUB_TOKEN}` } : {};

const ready = (page) => page.waitForFunction(() => document.documentElement.dataset.speculaReady === "true", null, { timeout: 60_000 });
const settled = (page) => page.locator(".app-status .ef-status-lozenge").filter({ hasNotText: /Connecting|Refreshing/ }).waitFor({ timeout: 60_000 });

test("deployed build identifies its source commit", async ({ page, request }) => {
  const build = await (await request.get("build.json")).json();
  expect(build.application).toBe("specula");
  if (EXPECT_SHA) expect(build.sourceSha).toBe(EXPECT_SHA);
  await page.goto("./#/about");
  await ready(page);
  await expect(page.locator("section[aria-label=\"About Specula\"] dd a.ef-identifier")).toHaveText(build.sourceSha);
});

test("live repository: real commits, pull requests and SHA-bound checks; selection survives reload", async ({ page, request }, testInfo) => {
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto("./");
  await ready(page);
  await page.getByLabel("GitHub repository").fill(REPO);
  await page.getByRole("button", { name: "Observe repository" }).click();
  await page.locator("h1", { hasText: "Overview" }).waitFor();
  await settled(page);
  await expect(page.locator(".app-status .ef-status-lozenge")).toHaveText(/Fresh|Partial/);

  const branch = (await (await request.get(`https://api.github.com/repos/${REPO}`, { headers: apiHeaders })).json()).default_branch;
  const head = (await (await request.get(`https://api.github.com/repos/${REPO}/commits/${branch}`, { headers: apiHeaders })).json()).sha;
  const shown = await page.locator("#ov-head-title").locator("..").locator("a.ef-identifier").textContent();
  // HEAD may advance between the two reads; accept only an exact prefix of the API's HEAD or report it.
  expect(head.startsWith(shown.trim()), `displayed HEAD ${shown} vs API HEAD ${head}`).toBe(true);
  await page.screenshot({ path: testInfo.outputPath("live-overview.png"), fullPage: true });

  await page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Activity", exact: true }).click();
  const feed = page.locator("section[aria-label=Activity] .app-event");
  await expect(feed.first()).toBeVisible();
  const href = await feed.first().getByRole("link", { name: /on GitHub/ }).getAttribute("href");
  expect(href).toMatch(new RegExp(`^https://github.com/${REPO}/`));
  const filters = await page.locator("section[aria-label=Activity] .ef-segment").allTextContents();
  const count = (label) => Number((filters.find((f) => f.startsWith(label)) ?? "(0)").match(/\((\d+)\)/)[1]);
  expect(count("Commits")).toBeGreaterThan(0);
  if (process.env.SPECULA_EXPECT_PULLS) expect(count("Pull requests")).toBeGreaterThan(0);
  if (process.env.SPECULA_EXPECT_CHECKS) expect(count("Checks")).toBeGreaterThan(0);
  await page.screenshot({ path: testInfo.outputPath("live-activity.png"), fullPage: true });

  await page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Verify", exact: true }).click();
  await expect(page.locator("#v-truth-title").locator("..")).toContainText("Application smoke test");
  await page.screenshot({ path: testInfo.outputPath("live-verification.png"), fullPage: true });

  await page.reload();
  await ready(page);
  await expect(page.locator(".app-repo__name")).toHaveText(REPO);
  await expect(page.locator("h1")).toHaveText("Verification");
  await settled(page);
  expect(errors).toEqual([]);
});

test("live repository on a 390px viewport has no horizontal overflow", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("./");
  await ready(page);
  await page.getByLabel("GitHub repository").fill(REPO);
  await page.getByRole("button", { name: "Observe repository" }).click();
  await page.locator("h1", { hasText: "Overview" }).waitFor();
  await settled(page);
  for (const screen of ["overview", "activity", "verification", "replay"]) {
    await page.goto(`./#/${screen}`);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, screen).toBeLessThanOrEqual(0);
    await page.screenshot({ path: testInfo.outputPath(`live-${screen}-390.png`), fullPage: true });
  }
});
