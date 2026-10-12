import { expect, test } from "@playwright/test";
import { layoutProblems } from "../support/layout";

/**
 * The layout checker itself (support/layout.ts) on the two layouts the owner reported on 12 Oct 2026, rebuilt in plain HTML: it must
 * report them, and must stay quiet on the corrected layouts, on text under a dialog, on text scrolled out of a list and on an ellipsis.
 */
const base = `<style>body{margin:0;font:14px/1.5 sans-serif} .stack{display:flex;flex-direction:column;min-height:0}</style>`;

test("checker: SCR-76 squeezed permission groups overlap", async ({ page }) => {
  const rows = Array.from({ length: 4 }, (_, i) => `<label style="display:flex;gap:6px"><input type="checkbox"><span>permission ${i} description text</span></label>`).join("");
  const groups = Array.from({ length: 8 }, (_, g) => `<fieldset class="stack"><legend>Group ${g}</legend>${rows}</fieldset>`).join("");
  const dialog = (fix: string) => `${base}<div style="display:flex;flex-direction:column;height:400px;width:600px">
    <div style="display:flex;flex-direction:column;overflow:auto;gap:8px">${groups.replaceAll("<fieldset", `<fieldset style="${fix}"`)}</div></div>`;
  await page.setContent(dialog(""));
  expect((await layoutProblems(page)).filter((p) => p.kind === "overlap").length).toBeGreaterThan(0);
  await page.setContent(dialog("flex-shrink:0"));
  expect(await layoutProblems(page)).toEqual([]);
});

test("checker: SCR-05 QR code over the instructions and the code field", async ({ page }) => {
  const qr = `<svg viewBox="0 0 10 10" width="240" height="240"><rect width="10" height="10" fill="#000"/></svg>`;
  const card = (box: string) => `${base}<div style="width:380px;display:flex;flex-direction:column;gap:8px">
    <div style="display:flex;gap:14px;align-items:center"><div style="${box}">${qr}</div>
    <div><div>1. Open your authenticator app</div><div>2. Scan the code, or enter the key:</div><div>WRYE 7FSC 5S67</div></div></div>
    <label>6-digit code</label><input></div>`;
  await page.setContent(card("width:132px;height:132px;flex:none;background:#fff"));
  expect((await layoutProblems(page)).length).toBeGreaterThan(0);
  await page.setContent(card("width:132px;height:132px;flex:none;background:#fff;overflow:hidden").replace('width="240" height="240"', 'style="width:100%;height:100%"'));
  expect(await layoutProblems(page)).toEqual([]);
});

test("checker: a one-line button whose label wraps", async ({ page }) => {
  const bar = (fix: string) => `${base}<button style="display:flex;align-items:center;height:32px;width:240px;${fix}">
    <span style="${fix ? "white-space:nowrap;overflow:hidden;text-overflow:ellipsis;min-width:0" : ""}">Search by trip, invoice number or driver name…</span></button>`;
  await page.setContent(bar(""));
  expect((await layoutProblems(page)).map((p) => p.kind)).toContain("cut-off");
  await page.setContent(bar("min-width:0"));
  expect(await layoutProblems(page)).toEqual([]);
});

test("checker: no false alarms for dialogs, scrolled lists and ellipsis", async ({ page }) => {
  await page.setContent(`${base}
    <p>Page text under the dialog</p><p>More page text</p>
    <div style="position:fixed;inset:0;background:rgba(0,0,0,.4);display:grid;place-items:center">
      <div style="background:#fff;width:300px;padding:16px">Dialog text that sits over the page<input style="width:100%"><textarea style="width:100%"></textarea></div></div>
    ${"<p>Page text that the dialog's fields cover</p>".repeat(6)}
    <div style="height:60px;overflow:auto;width:200px">${"<div>row in a scrolling list</div>".repeat(10)}</div>
    <div style="width:80px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">A long name shortened with an ellipsis</div>
    <div style="position:relative;width:160px"><input style="width:100%;box-sizing:border-box;padding-inline-end:32px">
      <span style="position:absolute;inset-inline-end:8px;top:2px">kg</span></div>
    <div style="height:90px;overflow:auto;width:240px"><table style="border-collapse:collapse">
      <tbody>${"<tr><td><input style='width:120px'></td></tr>".repeat(6)}</tbody>
      <tfoot style="position:sticky;bottom:0;background:#eee"><tr><td>Total 0.000</td></tr></tfoot></table></div>`);
  expect(await layoutProblems(page)).toEqual([]);
});
