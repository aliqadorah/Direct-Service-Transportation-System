import fs from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { ROOT } from "../support/env";

/**
 * DT-242: in the collapsed side menu, Escape hides a section's flyout (or name) shown because it is pointed at or focused — as it closes
 * one opened with Enter. Tested on its own in a bare page with the real ui.js and stylesheets — no application, no sign-in.
 */
const WWW = path.join(ROOT, "src", "DirectService.Transportation.Web.Client", "wwwroot");
const FILES: Record<string, [string, string]> = {
  "/ui.js": ["text/javascript", fs.readFileSync(path.join(WWW, "js", "ui.js"), "utf8")],
  "/tokens.css": ["text/css", fs.readFileSync(path.join(WWW, "css", "tokens.css"), "utf8")],
  "/app.css": ["text/css", fs.readFileSync(path.join(WWW, "css", "app.css"), "utf8")],
};

const sec = (id: string, name: string, items: string[]) => `<div class="sec" id="${id}">
  <button type="button" class="g" id="${id}-b"><span class="lbl">${name}</span></button>
  <div class="fly" id="${id}-f"><div class="ft">${name}</div>${items.length ? `<div class="pt">${items.map((i) => `<a class="it" href="#${i}">${i}</a>`).join("")}</div>` : ""}</div>
</div>`;

async function load(page: Page, dir: "rtl" | "ltr"): Promise<void> {
  await page.setViewportSize({ width: 1366, height: 680 });
  await page.route("https://menu.test/**", (route) => {
    const file = FILES[new URL(route.request().url()).pathname];
    return file ? route.fulfill({ contentType: file[0], body: file[1] }) : route.fulfill({ contentType: "text/html", body: `<!doctype html>
<html dir="${dir}" lang="${dir === "rtl" ? "ar" : "en"}"><head><link rel="stylesheet" href="/tokens.css"><link rel="stylesheet" href="/app.css"></head>
<body><div class="shell"><aside class="side mini"><nav id="nav">${sec("acc", "Accounting", ["Journal", "Trial balance"])}${sec("rep", "Reports", ["Saved reports"])}</nav></aside>
<div class="main"><main class="content" id="content">Page</main></div></div></body></html>` });
  });
  await page.goto("https://menu.test/");
  await page.evaluate(async () => {
    const ui = await import("https://menu.test/ui.js");
    ui.navFlyouts(document.getElementById("nav"));
  });
}

const visibility = (page: Page, id: string) => page.evaluate((i) => getComputedStyle(document.getElementById(i)!).visibility, id);

for (const dir of ["rtl", "ltr"] as const) {
  test.describe(`Collapsed side menu (${dir})`, () => {
    test("Escape hides a flyout shown by hover; it shows again when the pointer comes back", async ({ page }) => {
      await load(page, dir);
      await page.hover("#acc-b");
      await expect.poll(() => visibility(page, "acc-f")).toBe("visible");
      await expect(page.locator("#acc-f a").first()).toBeVisible();

      await page.keyboard.press("Escape");
      await expect.poll(() => visibility(page, "acc-f")).toBe("hidden");

      await page.hover("#content");
      await page.hover("#acc-b");
      await expect.poll(() => visibility(page, "acc-f")).toBe("visible");
    });

    test("Escape hides the name shown on focus; it shows again when the focus comes back", async ({ page }) => {
      await load(page, dir);
      await page.mouse.move(683, 600);
      await page.focus("#acc-b");
      await expect.poll(() => visibility(page, "acc-f")).toBe("visible");
      await page.keyboard.press("Escape");
      await expect.poll(() => visibility(page, "acc-f")).toBe("hidden");

      await page.focus("#rep-b");
      await expect.poll(() => visibility(page, "rep-f")).toBe("visible");
      await page.focus("#acc-b");
      await expect.poll(() => visibility(page, "acc-f")).toBe("visible");
    });

    test("A flyout opened with Enter is left to the menu (it closes it and returns the focus)", async ({ page }) => {
      await load(page, dir);
      await page.mouse.move(683, 600);
      await page.evaluate(() => document.getElementById("acc")!.classList.add("flown"));
      await page.focus("#acc-f a");
      await page.keyboard.press("Escape");
      expect(await page.evaluate(() => document.getElementById("acc")!.classList.contains("hush"))).toBe(false);
      await expect.poll(() => visibility(page, "acc-f")).toBe("visible");
    });
  });
}
