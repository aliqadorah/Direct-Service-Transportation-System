import fs from "node:fs";
import path from "node:path";
import { expect, type Page } from "@playwright/test";
import type { Lang } from "./auth";
import { ROOT } from "./env";
import { layoutProblems, type LayoutProblem } from "./layout";

/** Window sizes of the check (desktop is the target, UX-14) and a laptop-like visible height. */
export const WIDTHS: readonly number[] = process.env.VISUAL_WIDTHS ? process.env.VISUAL_WIDTHS.split(",").map(Number) : [1280, 1366];
export const DENSITIES = ["compact", "comfortable"] as const;
const HEIGHT = 680;

/** A layout problem at one size, or a screen/dialog that could not be shown (so it was not checked). */
export type Finding = Omit<LayoutProblem, "kind"> & { kind: LayoutProblem["kind"] | "not-shown"; screen: string; lang: Lang; width: number; density: string };

export const VISUAL_DIR = path.join(ROOT, "TestResults", "visual");

/**
 * Only the screens, dialogs and states whose name or address contains one of the comma-separated parts of VISUAL_ONLY (e.g.
 * `VISUAL_ONLY=reports/builder,builder#` with `VISUAL_WIDTHS=1366,1920`), to re-check the screens a change touched; unset = everything.
 */
export function wanted(...names: string[]): boolean {
  // Git Bash rewrites a value starting with "/" into a Windows path ("/tenant/x" → "C:/Program Files/Git/tenant/x"): undo it, or the
  // filter silently matches nothing (13 Oct 2026: the subscription screens were skipped and the console waited for a tenant row).
  const only = process.env.VISUAL_ONLY?.split(",").map((x) => x.trim().replace(/^[A-Za-z]:\/Program Files\/Git(?=\/)/i, "")).filter(Boolean);
  return !only || only.length === 0 || names.some((n) => only.some((o) => n.includes(o)));
}

/** Collects the problems of one run and writes them to TestResults/visual/problems-<part>.json as it goes. */
export class VisualRun {
  readonly findings: Finding[] = [];
  readonly shots: string[] = [];
  private readonly file: string;

  constructor(readonly lang: Lang, part: string, readonly screenshots = process.env.VISUAL_SHOTS !== "0") {
    fs.mkdirSync(VISUAL_DIR, { recursive: true });
    this.file = path.join(VISUAL_DIR, `problems-${part}-${lang}.json`);
  }

  /**
   * Checks the current screen at every width and density: first at a laptop-like height (dialogs, sticky parts, the visible area), then —
   * for full screens — at the height of the whole content, so that text below the fold is checked as well. Saves a screenshot of each.
   */
  async check(page: Page, screen: string, opts: { full?: boolean } = {}): Promise<void> {
    console.log(`visual ${new Date().toISOString().slice(11, 19)} ${this.lang} ${screen}`);
    for (const width of WIDTHS) {
      for (const density of DENSITIES) {
        await page.setViewportSize({ width, height: HEIGHT });
        await page.evaluate((d) => { document.documentElement.dataset.density = d; }, density);
        await settle(page);
        await this.record(page, screen, width, density, "");
        if (opts.full) {
          const tall = await page.evaluate(() => {
            const c = document.querySelector("main.content, .content, .auth .formside") as HTMLElement | null;
            return c ? Math.min(6000, window.innerHeight + Math.max(0, c.scrollHeight - c.clientHeight)) : window.innerHeight;
          });
          if (tall > HEIGHT + 8) {
            await page.setViewportSize({ width, height: tall });
            await settle(page);
            await this.record(page, screen, width, density, "-full");
          }
        }
      }
    }
    await page.setViewportSize({ width: 1366, height: 860 });
    await page.evaluate(() => { document.documentElement.dataset.density = localStorage.getItem("dst.density") === "comfortable" ? "comfortable" : "compact"; });
    fs.writeFileSync(this.file, JSON.stringify(this.findings, null, 2));
  }

  private async record(page: Page, screen: string, width: number, density: string, suffix: string): Promise<void> {
    for (const p of await layoutProblems(page)) this.findings.push({ ...p, screen, lang: this.lang, width, density });
    if (!this.screenshots) return;
    const file = path.join(VISUAL_DIR, this.lang, `${width}-${density}`, `${slug(screen)}${suffix}.png`);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    await page.screenshot({ path: file, animations: "disabled", caret: "hide" });
    this.shots.push(file);
  }

  /** The test fails on any problem; the message lists one line per problem (the same problem at several sizes is listed once). */
  assertClean(): void {
    const lines = [...new Set(this.findings.map((f) => `${f.screen} [${f.kind}] "${f.text}"${f.other ? ` × "${f.other}"` : ""} @ ${f.where}`))];
    expect(lines, lines.join("\n")).toEqual([]);
  }
}

/** Fonts loaded, layout and transitions finished. */
async function settle(page: Page): Promise<void> {
  await page.evaluate(async () => {
    await document.fonts.ready;
    await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
  });
  await page.waitForTimeout(120);
}

export function slug(s: string): string {
  return s.replace(/^\/c\/[^/]+\/?/, "").replace(/[^a-zA-Z0-9]+/g, "_").replace(/^_+|_+$/g, "") || "home";
}

/** Opens a screen and waits until it has rendered (title shown, skeletons gone); one reload when the PC is slow (low memory). */
export async function open(page: Page, route: string): Promise<boolean> {
  await page.goto(route, { timeout: 60_000 });
  const title = page.locator("main h1, h1").first();
  try {
    await expect(title).toBeVisible({ timeout: 20_000 });
  } catch {
    console.log(`visual: ${route} slow, reloading once`);
    await page.reload({ timeout: 60_000 });
    try {
      await expect(title).toBeVisible({ timeout: 40_000 });
    } catch {
      return false;
    }
  }
  await expect(page.locator(".sk")).toHaveCount(0, { timeout: 15_000 }).catch(() => undefined);
  return true;
}
