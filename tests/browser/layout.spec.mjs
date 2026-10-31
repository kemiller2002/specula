import { test, expect } from "@playwright/test";
import { install, scenarios, choose, settled, ready } from "./github-fixtures.mjs";

const widths = [320, 375, 390, 430, 768, 1024, 1440];
const screens = ["overview", "activity", "verification", "replay", "settings", "about", "select"];

// Geometry assertions: no horizontal page overflow, and no element inside a
// rounded/clipped card escapes its card's box.
const geometry = (page) =>
  page.evaluate(() => {
    const doc = document.documentElement;
    const overflow = doc.scrollWidth - doc.clientWidth;
    const escapes = [];
    for (const card of document.querySelectorAll(".app-card, .app-row, .ef-feed-item, .ef-metric-card, .ef-surface")) {
      if (card.offsetParent === null) continue;
      const box = card.getBoundingClientRect();
      for (const child of card.querySelectorAll("*")) {
        if (child.offsetParent === null) continue;
        const r = child.getBoundingClientRect();
        if (r.width === 0) continue;
        if (r.right > box.right + 1 || r.left < box.left - 1) escapes.push(`${child.tagName}.${child.className} in ${card.className}`);
      }
    }
    const clippedButtons = [...document.querySelectorAll("button, .ef-button, nav a")]
      .filter((b) => b.offsetParent !== null && b.getBoundingClientRect().right > doc.clientWidth + 1)
      .map((b) => b.textContent.trim());
    return { overflow, escapes: escapes.slice(0, 5), clippedButtons };
  });

for (const width of widths) {
  test(`layout at ${width}px: no overflow on any screen with long names`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await install(page, scenarios.longNames);
    await choose(page, "an-owner-with-a-long-name/" + "a-repository-name-that-is-extremely-long-".repeat(2).slice(0, 95));
    await settled(page);
    for (const screen of screens) {
      await page.goto(`/#/${screen}`);
      await page.waitForTimeout(150);
      const g = await geometry(page);
      expect(g.overflow, `${screen} horizontal overflow`).toBeLessThanOrEqual(0);
      expect(g.escapes, `${screen} content escaping cards`).toEqual([]);
      expect(g.clippedButtons, `${screen} clipped controls`).toEqual([]);
      if (width === 320 || width === 390 || width === 1440) {
        await page.screenshot({ path: testInfo.outputPath(`${screen}-${width}.png`), fullPage: true });
      }
    }
  });
}

test("200% text scaling at 390px keeps content contained", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await install(page, scenarios.longNames);
  await choose(page, "octo/demo");
  await settled(page);
  // CSSOM changes are permitted by the page's CSP (injected <style> is not).
  const scale = () => page.evaluate(() => { document.documentElement.style.fontSize = "200%"; });
  for (const screen of ["overview", "activity", "verification", "replay"]) {
    await page.goto(`/#/${screen}`);
    await scale();
    await page.waitForTimeout(100);
    const g = await geometry(page);
    expect(g.overflow, screen).toBeLessThanOrEqual(0);
    expect(g.clippedButtons, screen).toEqual([]);
  }
});

for (const scheme of ["light", "dark"]) {
  test(`${scheme} theme renders legible text on its surfaces`, async ({ page }, testInfo) => {
    await page.emulateMedia({ colorScheme: scheme });
    await install(page, scenarios.failedHead);
    await choose(page, "octo/demo");
    await settled(page);
    const contrast = await page.evaluate(() => {
      const parse = (c) => c.match(/[\d.]+/g).slice(0, 3).map(Number);
      const lum = ([r, g, b]) => [r, g, b].map((v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; }).reduce((a, v, i) => a + v * [0.2126, 0.7152, 0.0722][i], 0);
      const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
      const app = getComputedStyle(document.querySelector(".app"));
      return { ratio: ratio(parse(app.color), parse(app.backgroundColor)), background: lum(parse(app.backgroundColor)) };
    });
    expect(contrast.ratio).toBeGreaterThanOrEqual(4.5);
    // The theme must actually follow the system preference.
    if (scheme === "dark") expect(contrast.background).toBeLessThan(0.1);
    else expect(contrast.background).toBeGreaterThan(0.5);
    await page.screenshot({ path: testInfo.outputPath(`overview-${scheme}.png`), fullPage: true });
  });
}

test("explicit theme setting overrides the system preference", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" });
  await install(page, scenarios.passing);
  await choose(page, "octo/demo");
  await page.goto("/#/settings");
  await page.getByLabel("Theme").selectOption("dark");
  await page.waitForTimeout(200);
  const dark = await page.evaluate(() => getComputedStyle(document.querySelector(".app")).backgroundColor);
  await page.getByLabel("Theme").selectOption("light");
  await page.waitForTimeout(200);
  const light = await page.evaluate(() => getComputedStyle(document.querySelector(".app")).backgroundColor);
  expect(dark).not.toEqual(light);
});

test("keyboard: skip link, navigation and refresh are reachable with visible names", async ({ page }) => {
  await install(page, scenarios.passing);
  await choose(page, "octo/demo");
  await settled(page);
  await page.reload();
  await ready(page);
  await settled(page);
  await page.keyboard.press("Tab");
  await expect(page.locator(".ef-skip-link")).toBeFocused();
  const names = [];
  for (let i = 0; i < 12; i++) {
    await page.keyboard.press("Tab");
    names.push(await page.evaluate(() => (document.activeElement?.getAttribute("aria-label") || document.activeElement?.textContent || "").trim()));
  }
  expect(names).toEqual(expect.arrayContaining(["Refresh", "Overview", "Activity", "Verify", "Replay"]));
  await page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Activity", exact: true }).focus();
  await page.keyboard.press("Enter");
  await expect(page.locator("h1")).toHaveText("Activity");
});

test("accessibility basics: landmarks, labelled controls, one h1, live status", async ({ page }) => {
  await install(page, scenarios.passing);
  await choose(page, "octo/demo");
  await settled(page);
  for (const screen of ["overview", "activity", "verification", "replay", "settings", "about"]) {
    await page.goto(`/#/${screen}`);
    await expect(page.locator("h1:visible")).toHaveCount(1);
    const unlabeled = await page.evaluate(() =>
      [...document.querySelectorAll("button, a[href], input, select")]
        .filter((el) => el.offsetParent !== null)
        .filter((el) => {
          const label = el.getAttribute("aria-label") || el.textContent.trim() || (el.id && document.querySelector(`label[for="${el.id}"]`)?.textContent.trim()) || el.closest("label")?.textContent.trim();
          return !label;
        })
        .map((el) => el.outerHTML.slice(0, 80)));
    expect(unlabeled, screen).toEqual([]);
    const small = await page.evaluate(() =>
      [...document.querySelectorAll("button, nav a, select, input:not([type=radio])")]
        .filter((el) => el.offsetParent !== null)
        .filter((el) => el.getBoundingClientRect().height < 43.5)
        .map((el) => el.textContent.trim() || el.outerHTML.slice(0, 60)));
    expect(small, `${screen} touch targets`).toEqual([]);
  }
  await expect(page.locator("main")).toHaveCount(1);
  await expect(page.locator(".app-status")).toHaveAttribute("aria-live", "polite");
});
