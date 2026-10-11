import fs from "node:fs";
import path from "node:path";
import { ROOT } from "./env";
import type { Lang } from "./auth";

/**
 * The application's own AR/EN text catalogue (`Localization/UiText*.cs`, entries `["key"] = ("ar", "en")`), so a spec finds buttons and
 * labels by the same text a user reads, in either language.
 */
const catalogue = (() => {
  const dir = path.join(ROOT, "src", "DirectService.Transportation.Web.Client", "Localization");
  const map = new Map<string, { ar: string; en: string }>();
  const entry = /\["([^"]+)"\]\s*=\s*\("((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)"\)/g;
  for (const file of fs.readdirSync(dir).filter((f) => f.startsWith("UiText") && f.endsWith(".cs"))) {
    const text = fs.readFileSync(path.join(dir, file), "utf8");
    for (const m of text.matchAll(entry)) map.set(m[1], { ar: unescape(m[2]), en: unescape(m[3]) });
  }
  return map;
})();

function unescape(s: string): string {
  return s.replace(/\\"/g, '"').replace(/\\\\/g, "\\");
}

/** The text of a key in a language; `{0}`… replaced by the arguments. */
export function t(lang: Lang, key: string, ...args: (string | number)[]): string {
  const e = catalogue.get(key);
  if (!e) throw new Error(`Unknown UI text key ${key}`);
  return args.reduce<string>((s, a, i) => s.replace(`{${i}}`, String(a)), e[lang]);
}

/** A bound text function for one language. */
export const texts = (lang: Lang) => (key: string, ...args: (string | number)[]) => t(lang, key, ...args);
