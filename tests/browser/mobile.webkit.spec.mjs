import { test, expect } from "@playwright/test";
import { baseScenario, choose, install } from "./github-fixtures.mjs";

const expectNoHorizontalOverflow = async (page) => {
  const dimensions = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(dimensions.content).toBeLessThanOrEqual(dimensions.viewport + 1);
};

test.beforeEach(async ({ page }) => {
  await install(page, baseScenario());
  await choose(page, "octo/demo");
});

test("native date and time controls remain usable on iPhone WebKit", async ({ page }) => {
  await page.getByRole("link", { name: "Replay" }).click();
  await expect(page.getByRole("heading", { name: "Replay", level: 1 })).toBeVisible();

  const dateInputs = page.locator('input[type="datetime-local"]:visible');
  await expect(dateInputs).toHaveCount(2);

  for (const input of await dateInputs.all()) {
    const box = await input.boundingBox();
    expect(box).not.toBeNull();
    expect(box.height).toBeGreaterThanOrEqual(44);
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(390);
    await expect(input).toHaveAttribute("aria-describedby", /.+/);
  }

  await dateInputs.first().fill("2026-10-30T09:15");
  await expect(dateInputs.first()).toHaveValue("2026-10-30T09:15");
  await expectNoHorizontalOverflow(page);
});

test("settings controls stay legible and contained in dark mode on iPhone WebKit", async ({ page }) => {
  await page.getByRole("link", { name: "Settings" }).click();
  await page.getByLabel("Theme").selectOption("dark");
  await expect(page.locator(".app-theme")).toHaveAttribute("data-ef-theme", "dark");

  for (const control of await page.locator("select:visible, input:visible, button:visible").all()) {
    const box = await control.boundingBox();
    expect(box).not.toBeNull();
    expect(box.height).toBeGreaterThanOrEqual(44);
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(390);
  }

  const kickoff = page.getByLabel("Kickoff");
  const colors = await kickoff.evaluate((element) => {
    const style = getComputedStyle(element);
    return { background: style.backgroundColor, foreground: style.color };
  });
  expect(colors.background).not.toBe("rgba(0, 0, 0, 0)");
  expect(colors.foreground).not.toBe(colors.background);
  await expectNoHorizontalOverflow(page);
});
