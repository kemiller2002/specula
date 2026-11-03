import { test, expect } from "@playwright/test";
import { install, scenarios, choose, settled } from "./github-fixtures.mjs";

const nav = (page, name) => page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name, exact: true }).click();
const screen = (page) => page.locator('section.app-screen[aria-label="Requirement verification"]');
const fact = (page, label) => screen(page).locator("dt", { hasText: label }).locator("xpath=following-sibling::dd[1]");
const rowIds = (page) => page.locator("#rv-rows .ef-identifier").allTextContents();

const failing = ["INV-001", "INV-002", "INV-007", "INV-008", "INV-011", "INV-012", "INV-016"];

test("requirement verification shows the sample's counts, failing IDs and filters", async ({ page }) => {
  await install(page, scenarios.requirementVerification);
  await choose(page, "octo/demo");
  await settled(page);
  await nav(page, "Requirements");
  await expect(page.locator("h1")).toHaveText("Requirement verification");
  await expect(page).toHaveURL(/#\/requirements$/);

  await expect(fact(page, "Applicable requirement IDs")).toHaveText("17");
  await expect(fact(page, "Implemented IDs")).toHaveText("14");
  await expect(fact(page, "Verified IDs")).toHaveText("10");
  await expect(fact(page, "Failed IDs")).toHaveText("7");
  await expect(fact(page, "Blocked IDs")).toHaveText("0");
  await expect(fact(page, "Not-run IDs")).toHaveText("0");
  await expect(page.locator("#rv-headline")).toContainText("58.8%");
  await expect(page.locator("#rv-headline")).toContainText("Not complete: 10 of 17");
  await expect(page.getByText("100%")).toHaveCount(0);

  // Implemented and verified coverage are separate, distinctly labelled values.
  const verified = page.locator("#rv-verified-coverage");
  const implemented = page.locator("#rv-implemented-coverage");
  await expect(verified.locator("dt")).toHaveText("Verified coverage (verified ÷ applicable)");
  await expect(verified.locator("dd strong")).toHaveText("58.8%");
  await expect(implemented.locator("dt")).toHaveText("Implementation coverage (implemented ÷ applicable, not verification)");
  await expect(implemented.locator("dd strong")).toHaveText("82.3%");

  await expect(fact(page, "Source commit")).toHaveText("d6f5cb53ff3d43a158aad52a56ec696eba44dcb2");
  await expect(fact(page, "Manifest hash")).toHaveText("dd9a2a75b762891764ccc7fdf6fc7e7efe02135361e01d983ba8d9a8aa681c0c");
  await expect(page.locator("#rv-accepted")).toHaveText("Not passed (as recorded by the verifier)");
  await expect(fact(page, "Most recent verification")).toContainText("commit time of bbbbbbb");

  await expect(page.locator("#rv-unverified .ef-identifier")).toHaveText(failing);
  await expect(page.locator("#rv-unverified")).toContainText("Not passing: INV-016/V2/accessible");
  await page.locator("summary", { hasText: "Obligations not passing, with reasons" }).click();
  await expect(page.locator("#rv-obligations")).toContainText("<span> carrying INV-016 exposes no semantic role");

  expect(await rowIds(page)).toHaveLength(18);
  await page.locator("#rv-status").selectOption("FAIL");
  await expect(page.locator("#rv-shown")).toHaveText("Showing 7 of 18 requirements (filtered by status Fail).");
  expect(await rowIds(page)).toEqual(failing);
  await page.locator("#rv-screen").selectOption("investigation-start");
  await expect(page.locator("#rv-rows .ef-identifier")).toHaveText(["INV-001", "INV-002"]);
  await expect(page.locator("#rv-status")).toHaveValue("FAIL");
  await page.locator("#rv-type").selectOption("action");
  await expect(page.getByText("No requirement matches these filters.")).toBeVisible();
  await page.locator("#rv-status").selectOption("all");
  await expect(page.locator("#rv-rows .ef-identifier")).toHaveText(["INV-004"]);
  // The failing list is never filtered.
  await expect(page.locator("#rv-unverified .ef-identifier")).toHaveText(failing);
});

test("without a requirement verification report the screen says none was found, not zero", async ({ page }) => {
  await install(page, scenarios.passing);
  await choose(page, "octo/demo");
  await settled(page);
  await nav(page, "Requirements");
  await expect(page.locator("#rv-unavailable")).toContainText("No requirement verification evidence was found");
  await expect(page.locator("#rv-unavailable")).toContainText("unknown, not zero");
  await expect(page.locator("#rv-counts")).toBeHidden();
});
