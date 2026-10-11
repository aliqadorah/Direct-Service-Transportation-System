import fs from "node:fs";
import path from "node:path";
import { MAIL_DIR } from "./env";

/**
 * The newest e-mail of a template sent to an address, from the host's development drop folder (FileEmailSender writes
 * `<utc>-<Template>-<guid>.eml`), and the first https link in it. Polls briefly: sending happens right after the request.
 */
export async function linkFromMail(to: string, template: string, timeoutMs = 15_000): Promise<string> {
  const until = Date.now() + timeoutMs;
  for (;;) {
    const files = fs.existsSync(MAIL_DIR) ? fs.readdirSync(MAIL_DIR).filter((f) => f.includes(`-${template}-`)).sort().reverse() : [];
    for (const f of files) {
      const text = fs.readFileSync(path.join(MAIL_DIR, f), "utf8");
      if (!new RegExp(`^To: ${escape(to)}\\s*$`, "mi").test(text)) continue;
      const link = /href="(https:\/\/[^"]+)"/.exec(text)?.[1];
      if (link) return link.replace(/&amp;/g, "&");
    }
    if (Date.now() > until) throw new Error(`No ${template} e-mail to ${to}.`);
    await new Promise((r) => setTimeout(r, 500));
  }
}

function escape(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
