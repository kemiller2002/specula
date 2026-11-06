import { test, expect } from "@playwright/test";
import { install, scenarios, ready } from "./github-fixtures.mjs";

// The observer screen reads Specula's own static replay data
// (dist/observer/replay-009.json); it needs no repository selection and makes
// no GitHub request.
const screen = (page) => page.locator('section.app-screen[aria-label="Observer replay"]');

const open = async (page, githubLog) => {
  await install(page, scenarios.requirementVerification, { log: githubLog });
  await page.goto("/#/observer");
  await ready(page);
  await expect(page.locator("h1")).toHaveText("Observer replay");
  await expect(page.locator("#obs-elapsed")).not.toHaveText("");
};

const chooseCheckpoint = async (page, label) => {
  const option = page.locator("#obs-checkpoint option", { hasText: label }).first();
  await page.locator("#obs-checkpoint").selectOption(await option.getAttribute("value"));
};

test("the observer replay shows time, coverage, alerts, recovery and the next action", async ({ page }) => {
  const githubLog = [];
  await open(page, githubLog);
  await expect(screen(page).locator(".app-screen-intro")).toContainText("This is not live monitoring");
  await expect(page.locator("#obs-deadline")).toContainText("2026-11-04T07:30:01Z");

  await chooseCheckpoint(page, "T+115 · checkpoint T+115");
  await expect(page.locator("#obs-elapsed")).toHaveText("115 min");
  await expect(page.locator("#obs-remaining")).toHaveText("305 min");
  await expect(page.locator("#obs-head")).toHaveText("ade66fc");
  await expect(page.locator("#obs-verified")).toContainText("Frozen v4.1.2 plan: 0 of 161");
  await expect(page.locator("#obs-scope")).toContainText("of 194 mandatory requirements");
  await expect(page.locator("#obs-alerts")).toContainText("OBS-004");
  await expect(page.locator("#obs-alerts")).toContainText("Owner intervention recommended now");
  await expect(page.locator("#obs-blockers")).toContainText("Verification gate");
  await expect(page.locator("#obs-recovery")).toHaveText("Partial");
  await expect(page.locator("#obs-action")).toContainText("Intervene now");

  // Before the first application commit nothing is alerted.
  await chooseCheckpoint(page, "T+20 · checkpoint T+20");
  await expect(page.locator("#obs-remaining")).toHaveText("400 min");
  await expect(page.locator("#obs-counts")).toHaveText("0 critical · 0 warning");

  // Remote visibility sees the first commits only once they reach GitHub.
  await page.locator("#obs-mode").selectOption("remote");
  await chooseCheckpoint(page, "T+40 · checkpoint T+40");
  await expect(page.locator("#obs-head")).toHaveText("No builder commit visible");

  expect(githubLog.filter((u) => u.includes("github"))).toEqual([]);
});

test("the observer replay fits an iPhone-sized screen", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, []);
  await chooseCheckpoint(page, "T+115 · checkpoint T+115");
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  for (const id of ["#obs-mode", "#obs-checkpoint"]) {
    const box = await page.locator(id).boundingBox();
    expect(box.height).toBeGreaterThanOrEqual(44);
    expect(box.x + box.width).toBeLessThanOrEqual(390);
  }
  await page.setViewportSize({ width: 320, height: 640 });
  const narrow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(narrow).toBeLessThanOrEqual(0);
});
