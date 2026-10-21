import fs from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { ROOT } from "../support/env";

/**
 * DT-233: the shared keyboard-shortcut handler of every screen (wwwroot/js/ui.js), tested on its own in a bare page — no application, no
 * sign-in. Ctrl+S works by the physical key whatever the keyboard language (Arabic layout: e.key is "س"), commits the field being typed in
 * before pressing the save button, picks the open dialog's button first, and never lets the browser open its "Save page" dialog.
 */
const UI_JS = fs.readFileSync(path.join(ROOT, "src", "DirectService.Transportation.Web.Client", "wwwroot", "js", "ui.js"), "utf8");

async function load(page: Page, body: string): Promise<void> {
  await page.route("https://shortcuts.test/**", (route) => route.request().url().endsWith("/ui.js")
    ? route.fulfill({ contentType: "text/javascript", body: UI_JS })
    : route.fulfill({ contentType: "text/html", body: `<!doctype html><html><body>${body}</body></html>` }));
  await page.goto("https://shortcuts.test/");
  await page.evaluate(async () => {
    const ui = await import("https://shortcuts.test/ui.js");
    const w = window as unknown as { calls: string[]; log: string[] };
    w.calls = [];
    w.log = [];
    ui.registerShortcuts({ invokeMethodAsync: (name: string) => { w.calls.push(name); return Promise.resolve(); } });
  });
}

/** Sends a keydown like a keyboard with the given layout would; returns whether the default (browser) action was prevented. */
const press = (page: Page, init: KeyboardEventInit) => page.evaluate((i) => {
  const e = new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...i });
  (document.activeElement ?? document.body).dispatchEvent(e);
  return e.defaultPrevented;
}, init);

const log = (page: Page) => page.evaluate(() => (window as unknown as { log: string[] }).log);

const SAVE = `<form><input id="rate" /><button type="button" id="save" data-shortcut="Ctrl S">Save</button></form>
<script>
  const rate = document.getElementById("rate");
  rate.addEventListener("change", () => window.log.push("change:" + rate.value));
  document.getElementById("save").addEventListener("click", () => window.log.push("save"));
</script>`;

test.describe("Shortcuts (ui.js)", () => {
  test("Ctrl+S saves with the Arabic and the English layout and blocks the browser's Save page", async ({ page }) => {
    await load(page, SAVE);
    expect(await press(page, { key: "س", code: "KeyS", ctrlKey: true })).toBe(true);   // Arabic layout
    expect(await press(page, { key: "s", code: "KeyS", ctrlKey: true })).toBe(true);   // English layout
    expect(await press(page, { key: "S", code: "KeyS", metaKey: true })).toBe(true);    // ⌘S on a Mac
    expect(await log(page)).toEqual(["save", "save", "save"]);
  });

  test("the field being typed in is committed before saving", async ({ page }) => {
    await load(page, SAVE);
    await page.locator("#rate").focus();
    await page.locator("#rate").evaluate((el: HTMLInputElement) => { el.value = "4.250"; });   // typed, no change event yet
    await press(page, { key: "س", code: "KeyS", ctrlKey: true });
    expect(await log(page)).toEqual(["change:4.250", "save"]);
  });

  test("the open dialog's save button is pressed, never the page's behind it", async ({ page }) => {
    await load(page, `<button type="button" id="page" data-shortcut="Ctrl S">Save page</button>
      <div role="dialog" aria-modal="true"><button type="button" id="dialog" data-shortcut="Ctrl S">Save dialog</button></div>
      <script>
        document.getElementById("page").addEventListener("click", () => window.log.push("page"));
        document.getElementById("dialog").addEventListener("click", () => window.log.push("dialog"));
      </script>`);
    await press(page, { key: "s", code: "KeyS", ctrlKey: true });
    expect(await log(page)).toEqual(["dialog"]);
  });

  test("without a save button, or with it disabled, nothing is pressed but the browser dialog is still blocked", async ({ page }) => {
    await load(page, `<button type="button" id="save" data-shortcut="Ctrl S" disabled>Save</button>
      <script>document.getElementById("save").addEventListener("click", () => window.log.push("save"));</script>`);
    expect(await press(page, { key: "س", code: "KeyS", ctrlKey: true })).toBe(true);
    expect(await log(page)).toEqual([]);
    await load(page, "<p>No form here</p>");
    expect(await press(page, { key: "s", code: "KeyS", ctrlKey: true })).toBe(true);
  });

  test("AltGr (Ctrl+Alt) types a character and is not a shortcut; other shortcuts use the physical key too", async ({ page }) => {
    await load(page, SAVE);
    expect(await press(page, { key: "s", code: "KeyS", ctrlKey: true, altKey: true })).toBe(false);
    expect(await log(page)).toEqual([]);
    expect(await press(page, { key: "ن", code: "KeyK", ctrlKey: true })).toBe(true);    // Ctrl+K, Arabic layout
    expect(await press(page, { key: "ظ", code: "Slash" })).toBe(true);                   // "/" key, Arabic layout, outside a field
    expect(await page.evaluate(() => (window as unknown as { calls: string[] }).calls)).toEqual(["OpenCommandBar", "OpenCommandBar"]);
  });
});
