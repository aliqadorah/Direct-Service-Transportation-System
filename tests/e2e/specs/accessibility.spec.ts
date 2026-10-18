import fs from "node:fs";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { api, c } from "../support/api";
import { companyOf, firstSignIn, signUp, type Lang } from "../support/auth";
import { ROOT } from "../support/env";
import { addTrip, buildWorld, today } from "../support/world";

/**
 * WCAG 2.1 AA check (PRD UX, Build prompt "accessibility-review") of every screen, in Arabic RTL and English: axe-core rules tagged
 * wcag2a / wcag2aa / wcag21a / wcag21aa on each route of the application, with real documents so that detail screens show content.
 * The findings of every screen are written to TestResults/a11y-<lang>.json; the test fails on any violation.
 */
type Finding = { route: string; id: string; impact: string | null; help: string; nodes: number; sample: string[] };

async function audit(page: Page, route: string, findings: Finding[], file?: string): Promise<void> {
  console.log(`a11y ${new Date().toISOString().slice(11, 19)} ${route}`);
  await page.goto(route, { timeout: 60_000 });
  try {
    await expect(page.locator("main h1, h1").first()).toBeVisible({ timeout: 20_000 });
  } catch {
    findings.push({ route, id: "screen-did-not-render", impact: "critical", help: "No page title within 20 s", nodes: 0, sample: [] });
    return;
  }
  await expect(page.locator(".sk")).toHaveCount(0, { timeout: 15_000 }).catch(() => undefined);   // skeletons gone
  // The only frames are the sandboxed printout previews: no scripts may run in them, so axe (which injects itself into every frame
  // first) would wait forever. Their own requirement — a title naming the preview — is checked here, then each frame is replaced by
  // a placeholder for the page audit. The printouts themselves are documents, not screens.
  const untitled = await page.evaluate(() => {
    const frames = Array.from(document.querySelectorAll("iframe"));
    const missing = frames.filter((f) => !(f.getAttribute("title") ?? "").trim()).length;
    for (const f of frames) f.replaceWith(Object.assign(document.createElement("div"), { className: "frame-placeholder" }));
    return missing;
  });
  if (untitled > 0) findings.push({ route, id: "frame-title", impact: "serious", help: "Frames must have a title", nodes: untitled, sample: ["iframe"] });
  const analysis = new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const result = await Promise.race([analysis, new Promise<never>((_, reject) => setTimeout(() => reject(new Error(`axe timed out on ${route}`)), 90_000))]);
  for (const v of result.violations) {
    findings.push({ route, id: v.id, impact: v.impact ?? null, help: v.help, nodes: v.nodes.length, sample: v.nodes.slice(0, 3).map((n) => n.target.join(" ")) });
  }
  if (file) fs.writeFileSync(file, JSON.stringify(findings, null, 2));   // written as the audit goes, so a stopped run still reports
}

function report(lang: Lang, part: string, findings: Finding[]): void {
  const dir = path.join(ROOT, "TestResults");
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, `a11y-${part}-${lang}.json`), JSON.stringify(findings, null, 2));
}

for (const lang of ["ar", "en"] as Lang[]) {
  test(`WCAG 2.1 AA — account pages (${lang})`, async ({ page }) => {
    const findings: Finding[] = [];
    for (const route of ["/sign-in", "/sign-up", "/forgot-password"]) await audit(page, `${route}?lang=${lang}`, findings);
    report(lang, "account", findings);
    expect(findings, JSON.stringify(findings, null, 1)).toEqual([]);
  });

  test(`WCAG 2.1 AA — every company and tenant screen (${lang})`, async ({ page }) => {
    test.setTimeout(60 * 60_000);
    await firstSignIn(page, await signUp(page, lang, `A11y ${lang}`), lang);
    const company = companyOf(page);
    const w = await buildWorld(page, company);
    const t1 = await addTrip(page, w, `AX-1-${lang}`);
    const t2 = await addTrip(page, w, `AX-2-${lang}`);
    const t3 = await addTrip(page, w, `AX-3-${lang}`);
    const accounts = await api<{ cashBoxes: { id: string }[] }>(page, "GET", c(company, "accounting/money-accounts/options"));
    const cash = accounts.cashBoxes[0].id;
    const inv = await api<{ invoice: { id: string } }>(page, "POST", c(company, "invoices"),
      { customerId: w.customer, invoiceType: "Trip", invoiceDate: today(), tripIds: [t1, t2], discount: 0, extraFees: 0, approve: true });
    await api(page, "POST", c(company, "receipts"), { customerId: w.customer, receiptDate: today(), invoiceId: inv.invoice.id, amount: 50, paymentMethod: "Cash", cashBoxId: cash });
    await api(page, "POST", c(company, "advances"), { driverId: w.driver, advanceDate: today(), amount: 20, paymentMethod: "Cash", cashBoxId: cash });

    const first = async (p: string, pick: (j: any) => any[]) => { const j = await api<any>(page, "GET", c(company, p)); return (pick(j)[0]?.id as string | undefined) ?? null; };
    const receipt = await first("receipts", (j) => j.page.items);
    const journal = await first("accounting/journal-entries", (j) => j.items);
    const advance = await first("advances", (j) => j.page.items);

    const routes = [
      "", "setup", "profile",
      "trips", "trips/list", "trips/new", "trips/import", "trips/shortage", `trips/${t3}`,
      "invoices", "invoices/new", "invoices/manual", `invoices/${inv.invoice.id}`, "credit-notes", `credit-notes/new?invoice=${inv.invoice.id}`, "e-invoicing",
      "receipts", "receipts/new", receipt ? `receipts/${receipt}` : null,
      "driver-accounts", `driver-accounts/${w.driver}`, `driver-accounts/${w.driver}/advance`, `driver-accounts/${w.driver}/settle`,
      "driver-accounts/advances", advance ? `driver-accounts/advances/${advance}` : null, "driver-accounts/settlements",
      "partner-accounts", "partner-accounts/payments", "partner-accounts/payments/new",
      "accounting/chart", "accounting/chart/import", "accounting/opening-balances", "accounting/journal", "accounting/journal/new", "accounting/journal/import",
      journal ? `accounting/journal/${journal}` : null, "accounting/automatic", "accounting/statement", "accounting/trial-balance",
      "accounting/profit-loss", "accounting/balance-sheet", "accounting/payables", "accounting/payables/new", "accounting/receivables", "accounting/receivables/new",
      "accounting/cash-banks", "accounting/cost-centres", "accounting/periods", "accounting/maintenance",
      // Revision 6 part B (added in the release regression of 18 Oct 2026).
      "accounting/dashboard", "accounting/budgets", "accounting/budget-vs-actual", "accounting/journal-templates",
      "reports/operational", "reports/financial", "reports/builder", "reports/saved",
      "masters/drivers", "masters/drivers/new", `masters/drivers/${w.driver}`, "masters/customers", "masters/partners", "masters/materials",
      "masters/loading-sites", "masters/unloading-sites", "masters/ownership-types", "masters/tariffs", "masters/monthly-rates", "masters/import/customers",
      "settings/company", "settings/mapping", "settings/numbering", "settings/e-invoicing", "settings/invoice-layout", "settings/fields", "settings/users",
      "settings/audit-log", "help", "help/setup", "help/invoicing",
    ].filter((r): r is string => r !== null).map((r) => `/c/${company}${r ? `/${r}` : ""}`);
    routes.push("/tenant/subscription", "/tenant/companies", "/tenant/export", "/tenant/support-access");

    const findings: Finding[] = [];
    const file = path.join(ROOT, "TestResults", `a11y-screens-${lang}.json`);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    for (const route of routes) await audit(page, route, findings, file);
    report(lang, "screens", findings);
    expect(findings, JSON.stringify(findings.map((f) => `${f.route} ${f.id} (${f.nodes})`), null, 1)).toEqual([]);
  });
}
