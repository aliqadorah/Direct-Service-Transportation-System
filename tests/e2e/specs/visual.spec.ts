import fs from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { api, c } from "../support/api";
import { companyOf, signUp, submitForm, useLanguage, waitForApp, type Account, type Lang } from "../support/auth";
import { ROOT } from "../support/env";
import { totp } from "../support/totp";
import { staffSignIn } from "../support/staff";
import { texts } from "../support/text";
import { button } from "../support/ui";
import { open, VisualRun } from "../support/visual";
import { addTrip, buildWorld, today, type World } from "../support/world";

/** The help topics (D-18), read from the client's Help/HelpTopics.cs so that every help page is checked. */
const HelpTopics = {
  All: [...fs.readFileSync(path.join(ROOT, "src", "DirectService.Transportation.Web.Client", "Help", "HelpTopics.cs"), "utf8")
    .matchAll(/new\("([a-z-]+)", "9\.\d+",/g)].map((m) => m[1]),
};

/**
 * Visual layout check (owner feedback 12 Oct 2026, SCR-05 and SCR-76): every screen and dialog in Arabic RTL and English, at 1280 and
 * 1366 px wide, in compact and comfortable density. Fails on overlapping text, text cut off by its box and sideways page scroll
 * (support/layout.ts). Screenshots of every combination go to TestResults/visual/<lang>/<width>-<density>/ (VISUAL_SHOTS=0 skips them).
 */

/** Sign-up, then the first sign-in with the two-step set-up (SCR-05) and the recovery codes checked on the way. */
async function enrol(page: Page, lang: Lang, run: VisualRun): Promise<Account> {
  const account = await signUp(page, lang, `Visual ${lang}`);
  await page.context().clearCookies();
  await page.goto(`/sign-in?lang=${lang}`);
  await page.locator('[name="Input.Email"]').fill(account.email);
  await page.locator('[name="Input.Password"]').fill(account.password);
  await submitForm(page, "sign-in");
  await expect(page).toHaveURL(/two-factor\/setup/);
  await run.check(page, "two-factor-setup");
  const key = ((await page.locator('form .num[dir="ltr"]').first().textContent()) ?? "").replace(/\s/g, "");
  await page.locator('[name="Input.Code"]').fill(totp(key));
  await submitForm(page, "two-factor-setup");
  await expect(page.locator('ul.num[dir="ltr"] li')).toHaveCount(10);
  await run.check(page, "two-factor-recovery-codes");
  await page.locator("a.btn.pri.w100").click();
  await waitForApp(page);
  await useLanguage(page, lang);
  return { ...account, key };
}

/** Real documents so that detail screens show content (as in the accessibility check). */
async function documents(page: Page, company: string): Promise<{ w: World; ids: Record<string, string | null> }> {
  const w = await buildWorld(page, company);
  const t1 = await addTrip(page, w, `VS-1-${company.slice(0, 4)}`);
  const t2 = await addTrip(page, w, `VS-2-${company.slice(0, 4)}`);
  const t3 = await addTrip(page, w, `VS-3-${company.slice(0, 4)}`);
  const accounts = await api<{ cashBoxes: { id: string }[] }>(page, "GET", c(company, "accounting/money-accounts/options"));
  const cash = accounts.cashBoxes[0].id;
  const inv = await api<{ invoice: { id: string } }>(page, "POST", c(company, "invoices"),
    { customerId: w.customer, invoiceType: "Trip", invoiceDate: today(), tripIds: [t1, t2], discount: 0, extraFees: 0, approve: true });
  await api(page, "POST", c(company, "receipts"), { customerId: w.customer, receiptDate: today(), invoiceId: inv.invoice.id, amount: 50, paymentMethod: "Cash", cashBoxId: cash });
  await api(page, "POST", c(company, "advances"), { driverId: w.driver, advanceDate: today(), amount: 20, paymentMethod: "Cash", cashBoxId: cash });
  const first = async (p: string, pick: (j: any) => any[]) => { const j = await api<any>(page, "GET", c(company, p)); return (pick(j)[0]?.id as string | undefined) ?? null; };
  return {
    w,
    ids: {
      trip: t3, invoice: inv.invoice.id,
      receipt: await first("receipts", (j) => j.page.items),
      journal: await first("accounting/journal-entries", (j) => j.items),
      advance: await first("advances", (j) => j.page.items),
    },
  };
}

function companyRoutes(company: string, w: World, ids: Record<string, string | null>): string[] {
  return [
    "", "setup", "profile",
    "trips", "trips/list", "trips/new", "trips/import", "trips/shortage", `trips/${ids.trip}`,
    "invoices", "invoices/new", "invoices/manual", `invoices/${ids.invoice}`, "credit-notes", `credit-notes/new?invoice=${ids.invoice}`, "e-invoicing",
    "receipts", "receipts/new", ids.receipt ? `receipts/${ids.receipt}` : null,
    "driver-accounts", `driver-accounts/${w.driver}`, `driver-accounts/${w.driver}/advance`, `driver-accounts/${w.driver}/settle`,
    "driver-accounts/advances", "driver-accounts/advances/new", ids.advance ? `driver-accounts/advances/${ids.advance}` : null, "driver-accounts/settlements",
    "partner-accounts", "partner-accounts/payments", "partner-accounts/payments/new",
    "accounting/chart", "accounting/chart/import", "accounting/opening-balances", "accounting/journal", "accounting/journal/new", "accounting/journal/import",
    ids.journal ? `accounting/journal/${ids.journal}` : null, "accounting/automatic", "accounting/ledger", "accounting/statement", "accounting/trial-balance",
    "accounting/profit-loss", "accounting/balance-sheet", "accounting/payables", "accounting/payables/new", "accounting/receivables", "accounting/receivables/new",
    "accounting/cash-banks", "accounting/cost-centres", "accounting/periods", "accounting/maintenance",
    "reports/operational", "reports/financial", "reports/builder", "reports/saved",
    "masters/drivers", "masters/drivers/new", `masters/drivers/${w.driver}`, `masters/drivers/${w.driver}/edit`, "masters/customers", "masters/partners",
    "masters/materials", "masters/loading-sites", "masters/unloading-sites", "masters/ownership-types", "masters/tariffs", "masters/monthly-rates",
    "masters/import/customers",
    "settings/company", "settings/mapping", "settings/numbering", "settings/e-invoicing", "settings/invoice-layout", "settings/fields", "settings/users",
    "settings/audit-log",
    "help", ...HelpTopics.All.map((t) => `help/${t}`),
  ].filter((x): x is string => x !== null).map((x) => `/c/${company}${x ? `/${x}` : ""}`)
    .concat(["/tenant/subscription", "/tenant/companies", "/tenant/export", "/tenant/support-access"]);
}

for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — account pages (${lang})`, async ({ page }) => {
    test.setTimeout(20 * 60_000);
    const run = new VisualRun(lang, "account");
    for (const route of ["/sign-in", "/sign-up", "/forgot-password"]) {
      await page.goto(`${route}?lang=${lang}`);
      await run.check(page, route.slice(1), { full: true });
    }
    const account = await enrol(page, lang, run);
    // Sign-in with the code (SCR-04 second step).
    await page.context().clearCookies();
    await page.goto(`/sign-in?lang=${lang}`);
    await page.locator('[name="Input.Email"]').fill(account.email);
    await page.locator('[name="Input.Password"]').fill(account.password);
    await submitForm(page, "sign-in");
    await expect(page).toHaveURL(/two-factor/);
    await run.check(page, "two-factor");
    run.assertClean();
  });

  test(`Visual — every company and tenant screen (${lang})`, async ({ page }) => {
    test.setTimeout(90 * 60_000);
    const run = new VisualRun(lang, "screens");
    await enrol(page, lang, new VisualRun(lang, "screens-enrol", false));
    const company = companyOf(page);
    const { w, ids } = await documents(page, company);
    for (const route of companyRoutes(company, w, ids)) {
      if (!(await open(page, route))) {
        run.findings.push({ kind: "not-shown", text: "screen did not render", where: route, screen: route, lang, width: 0, density: "" });
        continue;
      }
      await run.check(page, route.replace(`/c/${company}`, "") || "dashboard", { full: true });
    }
    run.assertClean();
  });
}

/** A dialog, side panel or popup: the screen it is on and how a user opens it (only openers that write nothing). */
type Text = (key: string, ...args: (string | number)[]) => string;
type Popup = { name: string; route: string; open: (page: Page, t: Text) => Promise<void>; optional?: boolean };

const popupShown = (page: Page) => page.locator(".dialog, .panel-side, [role=menu], [role=listbox]").last();
const header = (key: string) => async (page: Page, t: Text) => { await button(page.locator("main"), t(key)).click(); };
const firstRow = async (page: Page) => { await page.locator("main table tbody tr").first().click(); };
const rolesTab = async (page: Page, t: Text) => { await page.getByRole("tab", { name: t("usr.roles") }).click(); };

function companyPopups(ids: Record<string, string | null>, w: World, roleName: string): Popup[] {
  const customRole = async (p: Page, t: Text) => { await rolesTab(p, t); await p.locator("main table tbody tr").filter({ hasText: roleName }).click(); };
  return [
    { name: "users#invite", route: "settings/users", open: header("usr.invite") },
    { name: "users#role-new", route: "settings/users", open: header("usr.newRole") },
    { name: "users#role-preset", route: "settings/users", open: async (p, t) => { await rolesTab(p, t); await firstRow(p); } },
    { name: "users#role-custom", route: "settings/users", open: customRole },
    { name: "users#role-delete", route: "settings/users", open: async (p, t) => { await customRole(p, t); await button(p.locator(".dialog"), t("ml.delete")).click(); } },
    { name: "users#user-panel", route: "settings/users", open: firstRow },
    { name: "payables#new-supplier", route: "accounting/payables", open: header("ap.newSupplier") },
    { name: "receivables#new-customer", route: "accounting/receivables", open: header("ap.newCustomer") },
    { name: "cash-banks#new-box", route: "accounting/cash-banks", open: header("cb.newBox") },
    { name: "cash-banks#new-bank", route: "accounting/cash-banks", open: header("cb.newBank") },
    { name: "cost-centres#new", route: "accounting/cost-centres", open: header("cc.new") },
    { name: "opening-balances#post", route: "accounting/opening-balances", open: header("acc.postOpening"), optional: true },
    { name: "periods#close", route: "accounting/periods", open: async (p, t) => { await button(p.locator("main table"), t("per.close")).click(); } },
    { name: "receipt#void", route: `receipts/${ids.receipt}`, open: header("inv.void") },
    { name: "advance#void", route: `driver-accounts/advances/${ids.advance}`, open: header("inv.void") },
    { name: "invoice#void", route: `invoices/${ids.invoice}`, open: header("inv.void") },
    { name: "trip#void", route: `trips/${ids.trip}`, open: header("trip.void") },
    { name: "driver#delete", route: `masters/drivers/${w.driver}`, open: header("ml.delete") },
    { name: "customers#new", route: "masters/customers", open: header("md.newCustomer") },
    { name: "customers#panel", route: "masters/customers", open: firstRow },
    { name: "customers#delete", route: "masters/customers", open: async (p, t) => { await p.locator("main table tbody tr").first().getByRole("button", { name: t("ml.delete") }).click(); } },
    { name: "materials#new", route: "masters/materials", open: header("md.newMaterial") },
    { name: "partners#new", route: "masters/partners", open: header("md.newPartner") },
    { name: "ownership-types#new", route: "masters/ownership-types", open: header("md.newType") },
    { name: "loading-sites#new", route: "masters/loading-sites", open: header("md.newSite") },
    { name: "tariffs#new", route: "masters/tariffs", open: header("tar.new") },
    { name: "fields#new", route: "settings/fields", open: header("fld.newCustom") },
    { name: "numbering#edit", route: "settings/numbering", open: firstRow },
    { name: "company#migration", route: "settings/company", optional: true, open: async (p, t) => {
      await p.getByRole("tab", { name: t("set.operations") }).click(); await button(p.locator("main"), t("set.migrationComplete")).click(); } },
    { name: "trips-list#columns", route: "trips/list", open: header("cols.title") },
    { name: "trip-new#driver-list", route: "trips/new", open: async (p, t) => {
      const input = p.getByRole("combobox", { name: t("trip.driver"), exact: true }).first(); await input.click(); await input.press("ArrowDown"); } },
    { name: "shell#command-bar", route: "", open: async (p) => { await p.locator("main h1").first().click(); await p.keyboard.press("Control+k"); } },
    { name: "shell#notifications", route: "", open: async (p, t) => {
      await p.getByRole("button", { name: new RegExp(`^${t("shell.notifications", "").replace(/\s*\(\)$/, "")}`) }).click(); } },
    { name: "shell#user-menu", route: "", open: async (p, t) => { await p.getByRole("button", { name: t("shell.userMenu") }).click(); } },
    { name: "tenant-companies#new", route: "/tenant/companies", open: header("ten.newCompany") },
    { name: "tenant-subscription#cancel", route: "/tenant/subscription", open: header("ten.cancelSubscription") },
  ];
}

async function sweepPopups(page: Page, run: VisualRun, lang: Lang, company: string | null, popups: Popup[]): Promise<void> {
  const t = texts(lang);
  for (const p of popups) {
    const route = p.route.startsWith("/") ? p.route : `/c/${company}${p.route ? `/${p.route}` : ""}`;
    const fail = (why: string) => run.findings.push({ kind: "not-shown", text: `not opened: ${why}`, where: route, screen: p.name, lang, width: 0, density: "" });
    if (!(await open(page, route))) { fail("screen did not render"); continue; }
    try {
      await p.open(page, t);
      await expect(popupShown(page)).toBeVisible({ timeout: 10_000 });
      await expect(page.locator(".sk")).toHaveCount(0, { timeout: 10_000 }).catch(() => undefined);
    } catch (e) {
      if (p.optional) console.log(`visual: optional ${p.name} not available here`);
      else fail(String(e).split("\n")[0].slice(0, 160));
      continue;
    }
    await run.check(page, p.name);
  }
}

for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — dialogs, panels and menus (${lang})`, async ({ page }) => {
    test.setTimeout(60 * 60_000);
    const run = new VisualRun(lang, "dialogs");
    await enrol(page, lang, new VisualRun(lang, "dialogs-enrol", false));
    const company = companyOf(page);
    const { w, ids } = await documents(page, company);
    // A custom role (preset roles open read-only and cannot be deleted) holding every key but the tenant one.
    const roleName = lang === "ar" ? "مدقق الحسابات" : "Auditor";
    const keys = (await api<{ key: string }[]>(page, "GET", c(company, "roles/permissions"))).map((p) => p.key);
    await api(page, "POST", c(company, "roles"), { name: roleName, permissions: keys.filter((k) => k !== "core.tenant.manage"), rowVersion: null });
    await sweepPopups(page, run, lang, company, companyPopups(ids, w, roleName));
    run.assertClean();
  });

  test(`Visual — provider console (${lang})`, async ({ page }) => {
    test.setTimeout(30 * 60_000);
    const run = new VisualRun(lang, "console");
    await staffSignIn(page, lang);
    for (const route of ["/console/tenants", "/console/plans", "/console/announcements", "/console/support-access", "/console/health"]) {
      if (await open(page, route)) await run.check(page, route.slice(1), { full: true });
      else run.findings.push({ kind: "not-shown", text: "screen did not render", where: route, screen: route, lang, width: 0, density: "" });
    }
    await sweepPopups(page, run, lang, null, [
      { name: "console-tenants#panel", route: "/console/tenants", open: firstRow },
      { name: "console-tenants#extend", route: "/console/tenants", optional: true, open: async (p, t) => {
        await firstRow(p); await button(p.locator(".panel-side"), t("con.extendTrial")).click(); } },
      { name: "console-plans#new", route: "/console/plans", open: header("con.newPlan") },
      { name: "console-announcements#new", route: "/console/announcements", open: header("con.newAnnouncement") },
      { name: "console-support#request", route: "/console/support-access", open: header("con.requestSupport") },
    ]);
    run.assertClean();
  });
}
