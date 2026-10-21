import fs from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { api, c } from "../support/api";
import { companyOf, signUp, submitForm, useLanguage, waitForApp, type Account, type Lang } from "../support/auth";
import { createHmac } from "node:crypto";
import { BASE_URL, ROOT, WEBHOOK_SECRET } from "../support/env";
import { linkFromMail } from "../support/mail";
import { totp } from "../support/totp";
import { staffSignIn } from "../support/staff";
import { texts } from "../support/text";
import { button, pick } from "../support/ui";
import { open, VisualRun, wanted } from "../support/visual";
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
  await page.locator('[name="Input.Code"]').fill(await totp(key));
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
  // Revision 6: a journal draft (no number) with a reference, so the draft form and the Drafts view have content.
  // Revision 6: two leaf accounts with movements for the statement of several accounts.
  const chart = await api<{ id: string; code: string }[]>(page, "GET", c(company, "accounting/accounts"));
  const statementAccounts = chart.filter((x) => x.code === "110101" || x.code === "110301").map((x) => x.id).join(",");
  // Revision 6: a supplier bill with a line deduction.
  const code = (c2: string) => chart.find((x) => x.code === c2)!.id;
  const supplier = await api<{ id: string }>(page, "POST", c(company, "apar/parties"),
    { role: "Supplier", name: "Visual supplier / مورد", taxNumber: null, phone: null, address: null, controlAccountId: code("2101"), paymentTermsDays: 30, clientId: null, rowVersion: null });
  const bill = await api<{ document: { id: string } }>(page, "POST", c(company, "apar/bills"), { partyId: supplier.id, documentDate: today(), reference: "DED-1",
    lines: [{ description: "Haulage / نقل", accountId: code("5103"), amount: 1000, taxRate: 16, deduction: 100 }, { description: "Parts / قطع", accountId: code("5104"), amount: 500, taxRate: 16 }] });
  // Owner decision 17 Oct 2026: a return on that bill gives back its share of the deduction.
  const billReturn = await api<{ return: { id: string } }>(page, "POST", c(company, "apar/returns"), { documentId: bill.document.id, returnDate: today(), amount: 300, reason: "Visual return / مرتجع" });
  const draft = await api<{ id: string }>(page, "POST", c(company, "accounting/journal-drafts"),
    { entryDate: today(), description: "Visual draft / مسودة", reference: "REF-1001", lines: [] });
  // Revision 6 part B: an approved budget of this year (revenue, a parent expense account and a posting account) for the budget page,
  // Budget vs Actual and the dashboard's budget usage; a journal template for the templates list.
  const year = Number(today().slice(0, 4));
  const every = (v: number) => Array.from({ length: 12 }, () => v);
  let budget = await api<{ id: string; rowVersion: string; name: string }>(page, "POST", c(company, "accounting/budgets"), { fiscalYear: year, name: `Budget ${year} / موازنة` });
  budget = await api<{ id: string; rowVersion: string; name: string }>(page, "PUT", c(company, `accounting/budgets/${budget.id}`), { name: budget.name, rowVersion: budget.rowVersion, lines: [
    { accountId: code("4101"), costCentreId: null, months: every(1500) },
    { accountId: code("51"), costCentreId: null, months: every(400) },
    { accountId: code("5202"), costCentreId: null, months: every(300) },
  ] });
  await api(page, "POST", c(company, `accounting/budgets/${budget.id}/approve`), { rowVersion: budget.rowVersion });
  const template = await api<{ id: string }>(page, "POST", c(company, "accounting/journal-templates"), {
    name: "Monthly rent / إيجار شهري", description: "Office rent / إيجار المكتب", reference: "Contract 12",
    lines: [{ accountId: code("5202"), debit: 300, credit: 0, costCentreId: null, note: "Rent / إيجار" }, { accountId: code("110101"), debit: 0, credit: 300, costCentreId: null, note: null }] });
  return {
    w,
    ids: {
      trip: t3, invoice: inv.invoice.id, invoicedTrip: t1,
      receipt: await first("receipts", (j) => j.page.items),
      journal: await first("accounting/journal-entries?status=Posted", (j) => j.items),
      draft: draft.id,
      statement: statementAccounts,
      bill: bill.document.id,
      billReturn: billReturn.return.id,
      budget: budget.id,
      template: template.id,
      advance: await first("advances", (j) => j.page.items),
    },
  };
}

function companyRoutes(company: string, w: World, ids: Record<string, string | null>): string[] {
  return [
    "", "setup", "profile",
    "trips", "trips/list", "trips/new", "trips/import", "trips/shortage", `trips/${ids.trip}`,
    "invoices", "invoices/new", "invoices/manual", `invoices/${ids.invoice}`, "credit-notes", `credit-notes/new?invoice=${ids.invoice}`, "e-invoicing",
    "receipts", "receipts/new", `receipts/new?customer=${w.customer}&invoice=${ids.invoice}`, ids.receipt ? `receipts/${ids.receipt}` : null,
    "driver-accounts", `driver-accounts/${w.driver}`, `driver-accounts/${w.driver}/advance`, `driver-accounts/${w.driver}/settle`,
    "driver-accounts/advances", "driver-accounts/advances/new", ids.advance ? `driver-accounts/advances/${ids.advance}` : null, "driver-accounts/settlements",
    "partner-accounts", "partner-accounts/payments", "partner-accounts/payments/new",
    "accounting/chart", "accounting/chart/import", "accounting/opening-balances", "accounting/journal", "accounting/journal/new", "accounting/journal/import",
    ids.journal ? `accounting/journal/${ids.journal}` : null, ids.draft ? `accounting/journal/drafts/${ids.draft}` : null, "accounting/automatic", "accounting/statement", ids.statement ? `accounting/statement?accounts=${ids.statement}` : null, "accounting/trial-balance",
    "accounting/profit-loss", "accounting/balance-sheet", "accounting/payables", "accounting/payables/new", ids.bill ? `accounting/payables/${ids.bill}` : null, ids.billReturn ? `accounting/apar-returns/${ids.billReturn}` : null, "accounting/receivables", "accounting/receivables/new",
    "accounting/cash-banks", "accounting/cost-centres", "accounting/periods", "accounting/maintenance",
    // Revision 6 part B.
    "accounting/dashboard", "accounting/budgets", ids.budget ? `accounting/budgets/${ids.budget}` : null, "accounting/budget-vs-actual",
    "accounting/journal-templates", ids.template ? `accounting/journal-templates/${ids.template}` : null,
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
    test.skip(!wanted("account", "/sign-in", "/sign-up", "two-factor", "change-password", "verify-email"), "not among VISUAL_ONLY");
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
    // Revision 6: a user created with a username and "must change password" → the forced password change page.
    await page.locator('[name="Input.Code"]').fill(await totp(account.key!));
    await submitForm(page, "two-factor");
    await waitForApp(page);
    const company = companyOf(page);
    const roles = await api<{ id: string; name: string }[]>(page, "GET", c(company, "roles"));
    const userName = `vis.${Date.now().toString(36)}`;
    await api(page, "POST", c(company, "users"), { fullName: "Visual user", userName, email: null, password: account.password,
      role: roles.find((r) => r.name === "Viewer")!.id, mustChangePassword: true });
    await page.context().clearCookies();
    await page.goto(`/sign-in?lang=${lang}`);
    await page.locator('[name="Input.Email"]').fill(userName);
    await page.locator('[name="Input.Password"]').fill(account.password);
    await submitForm(page, "sign-in");
    await expect(page).toHaveURL(/change-password/);
    await run.check(page, "change-password");
    // Owner decisions of 18 Oct 2026: the ordinary failure message on the sign-in page (also while an address is blocked, DT-204) and
    // the verification page of an administrator-entered email asking to sign in first (DT-203).
    await page.context().clearCookies();
    await page.goto(`/sign-in?lang=${lang}`);
    await page.locator('[name="Input.Email"]').fill(userName);
    await page.locator('[name="Input.Password"]').fill("Wrong-passw0rd");
    await submitForm(page, "sign-in");
    await expect(page.locator(".errsum, .banner").first()).toBeVisible();
    await run.check(page, "sign-in#failed");
    await page.goto(`/sign-in?lang=${lang}`);
    await page.locator('[name="Input.Email"]').fill(account.email);
    await page.locator('[name="Input.Password"]').fill(account.password);
    await submitForm(page, "sign-in");
    await page.locator('[name="Input.Code"]').fill(await totp(account.key!));
    await submitForm(page, "two-factor");
    await waitForApp(page);
    const mailUser = `vis.m${Date.now().toString(36)}`, mail = `${mailUser}@e2e.test`;
    await api(page, "POST", c(company, "users"), { fullName: "Visual mail user", userName: mailUser, email: mail, password: account.password,
      role: roles.find((r) => r.name === "Viewer")!.id, mustChangePassword: false });
    await page.context().clearCookies();
    await page.goto(`${await linkFromMail(mail, "VerifyEmail")}&lang=${lang}`);
    await submitForm(page, "verify");
    await expect(page.locator('a.btn.pri[href*="/sign-in"]')).toBeVisible();
    await run.check(page, "verify-email#sign-in");
    run.assertClean();
  });

  // Owner testing 20 Oct 2026 (DT-221): a sign-in form opened before the browser lost its antiforgery cookie (restart, sign-in or sign-out
  // in another tab) is shown again with "This page expired. Please try again." and the login kept — never the framework's raw text.
  test(`Visual — sign-in page expired (${lang})`, async ({ page }) => {
    test.skip(!wanted("sign-in#expired"), "not among VISUAL_ONLY");
    const run = new VisualRun(lang, "sign-in-expired");
    await page.goto(`/sign-in?lang=${lang}`);
    await page.locator('[name="Input.Email"]').fill("someone@e2e.test");
    await page.locator('[name="Input.Password"]').fill("Any-passw0rd");
    await page.context().clearCookies();
    await submitForm(page, "sign-in");
    await expect(page.locator(".banner.warn")).toBeVisible();
    await expect(page.locator('[name="Input.Email"]')).toHaveValue("someone@e2e.test");
    await expect(page.locator('[name="Input.Password"]')).toHaveValue("");
    await expect(page.locator("body")).not.toContainText("antiforgery");
    await run.check(page, "sign-in#expired", { full: true });
    run.assertClean();
  });

  test(`Visual — every company and tenant screen (${lang})`, async ({ page }) => {
    const states = (company: string, w: World, ids: Record<string, string | null>) => companyStates(ids, w).filter((x) => wanted(x.name, x.route));
    {
      const ids = { trip: "t", invoice: "i", receipt: "r", journal: "j", advance: "a", draft: "d", statement: "s", bill: "b", billReturn: "br" };
      const w = { driver: "d" } as World;
      const any = companyRoutes("x", w, ids).some((r) => wanted(r.replace("/c/x", "") || "dashboard")) || companyStates(ids, w).some((x) => wanted(x.name, x.route));
      test.skip(!any, "not among VISUAL_ONLY");
    }
    test.setTimeout(90 * 60_000);
    const run = new VisualRun(lang, "screens");
    await enrol(page, lang, new VisualRun(lang, "screens-enrol", false));
    const company = companyOf(page);
    const { w, ids } = await documents(page, company);
    if (wanted("/tenant/subscription")) await planChosen(page, lang);
    if (wanted("/tenant/support-access")) await supportRequested(page, lang);
    for (const route of companyRoutes(company, w, ids)) {
      if (!wanted(route.replace(`/c/${company}`, "") || "dashboard")) continue;
      if (!(await open(page, route))) {
        run.findings.push({ kind: "not-shown", text: "screen did not render", where: route, screen: route, lang, width: 0, density: "" });
        continue;
      }
      await run.check(page, route.replace(`/c/${company}`, "") || "dashboard", { full: true });
    }
    await sweepStates(page, run, lang, company, states(company, w, ids));
    // Owner testing 19 Oct 2026 (DT-213): the approve and refuse dialogs of the pending request made above (opened, never confirmed).
    await sweepPopups(page, run, lang, company, [
      { name: "tenant-support#approve", route: "/tenant/support-access", open: async (p, t) => { await button(p.locator("main"), t("ten.approve")).first().click(); } },
      { name: "tenant-support#refuse", route: "/tenant/support-access", open: async (p, t) => { await button(p.locator("main"), t("ten.reject")).first().click(); } },
    ]);
    run.assertClean();
  });
}

/**
 * Subscription page with content (owner feedback 13 Oct 2026, second round): staff publish a plan, the trial tenant chooses it and leaves
 * the payment page unpaid — the plans table, the tax notes and an invoice awaiting payment are on screen.
 */
async function planChosen(page: Page, lang: Lang): Promise<void> {
  const staff = await page.context().browser()!.newPage({ baseURL: BASE_URL, ignoreHTTPSErrors: true });
  try {
    await staffSignIn(staff, lang);
    const plan = await api<{ id: string }>(staff, "POST", "/api/v1/platform/plans", {
      code: `V${lang.toUpperCase()}${Date.now() % 100000}`, nameAr: "الخطة المهنية", nameEn: "Professional", price: 232, billingCycle: "Monthly",
      maxCompanies: 5, maxActiveUsers: 25, maxTripsPerMonth: 20000, maxStorageMb: 10240, isActive: true, rowVersion: null });
    // Two checkouts cancelled by staff and then paid from their old page: payments to refund (DT-183, DT-184); the first one is marked
    // refunded (DT-185), the second stays to refund…
    type Checkout = { invoice: { id: string; number: string; total: number; gatewayReference: string } };
    const latePayment = async () => {
      const c = await api<Checkout>(page, "POST", "/api/v1/tenant/subscription/checkout", { planId: plan.id });
      const hit = (await api<{ hits: { type: string; id: string }[] }>(staff, "GET", `/api/v1/platform/search?q=${c.invoice.number}`)).hits.find((h) => h.type === "subscriptionInvoice")!;
      await api(staff, "POST", `/api/v1/platform/tenants/${hit.id}/invoices/${c.invoice.id}/cancel`, { reason: "Visual check" });
      const body = JSON.stringify({ reference: c.invoice.gatewayReference, status: "succeeded", metadata: {}, amount: c.invoice.total, currency: "JOD" });
      const gateway = await page.context().browser()!.newContext({ baseURL: BASE_URL, ignoreHTTPSErrors: true });   // as the gateway: no session
      const paid = await gateway.request.post("/api/v1/billing/webhook", { data: body, headers: { "Content-Type": "application/json",
        "X-Signature": createHmac("sha256", WEBHOOK_SECRET).update(body).digest("hex").toUpperCase() } });
      await gateway.close();
      if (!paid.ok()) throw new Error(`webhook → ${paid.status()}`);
      return c;
    };
    const refunded = await latePayment();
    await api(staff, "POST", `/api/v1/platform/refunds/${refunded.invoice.id}/refunded`, { refundedOn: today(), reference: "TRX-778" });
    await latePayment();
    // …and the current checkout, awaiting payment.
    await api(page, "POST", "/api/v1/tenant/subscription/checkout", { planId: plan.id });
  } finally {
    await staff.close();
  }
}

/** A pending support access request from the provider for the signed-in tenant (owner testing 19 Oct 2026): the support page shows it. */
async function supportRequested(page: Page, lang: Lang): Promise<void> {
  const staff = await page.context().browser()!.newPage({ baseURL: BASE_URL, ignoreHTTPSErrors: true });
  try {
    await staffSignIn(staff, lang);
    const me = await api<{ email: string }>(page, "GET", "/api/v1/me");
    const found = await api<{ id: string; ownerEmail: string | null }[]>(staff, "GET", `/api/v1/platform/support-access/tenants?q=${encodeURIComponent(me.email)}`);
    const tenant = found.find((x) => x.ownerEmail === me.email)!;
    await api(staff, "POST", "/api/v1/platform/support-access", { tenantId: tenant.id, reason: lang === "ar" ? "فاتورة لا تُطبع" : "An invoice does not print" });
  } finally {
    await staff.close();
  }
}

/** A dialog, side panel or popup: the screen it is on and how a user opens it (only openers that write nothing). */
type Text = (key: string, ...args: (string | number)[]) => string;
type Popup = { name: string; route: string; open: (page: Page, t: Text) => Promise<void>; optional?: boolean };

const popupShown = (page: Page) => page.locator(".dialog, .panel-side, [role=menu], [role=listbox]").last();
const header = (key: string) => async (page: Page, t: Text) => { await button(page.locator("main"), t(key)).click(); };
const firstRow = async (page: Page) => { await page.locator("main table tbody tr").first().click(); };
/** Opens tenant rows until a panel offers "Cancel invoice" (an invoice awaiting payment); returns that button. */
const awaitingPanel = async (page: Page, t: Text) => {
  const rows = page.locator("main table tbody tr");
  const cancel = page.locator(".panel-side tbody button").filter({ hasText: t("con.cancelInvoice") }).first();
  for (let i = 0, n = await rows.count(); i < n; i++) {
    await rows.nth(i).click();
    if (await cancel.waitFor({ timeout: 3_000 }).then(() => true, () => false)) return cancel;
  }
  throw new Error("no tenant with an invoice awaiting payment");
};
const rolesTab = async (page: Page, t: Text) => { await page.getByRole("tab", { name: t("usr.roles") }).click(); };

function companyPopups(ids: Record<string, string | null>, w: World, roleName: string): Popup[] {
  const customRole = async (p: Page, t: Text) => { await rolesTab(p, t); await p.locator("main table tbody tr").filter({ hasText: roleName }).click(); };
  return [
    { name: "users#add-invite", route: "settings/users", open: header("usr.addUser") },
    { name: "users#add-create", route: "settings/users", open: async (p, t) => { await header("usr.addUser")(p, t); await p.locator(".dialog").getByRole("tab", { name: t("usr.modeCreate") }).click(); } },
    // Owner testing 18 Oct 2026 (DT-208): the owner's entries with a 7-character password, submitted — errors on their fields, rules shown.
    { name: "users#add-create-errors", route: "settings/users", open: async (p, t) => {
      await header("usr.addUser")(p, t);
      const d = p.locator(".dialog");
      await d.getByRole("tab", { name: t("usr.modeCreate") }).click();
      await d.locator("#new-name").fill("Viewer - مطلع");
      await d.locator("#new-username").fill("Viewer0");
      await d.locator("#new-password").fill("Abcdef1");
      await d.getByRole("combobox", { name: t("usr.role") }).selectOption({ index: 1 });
      await button(p, t("usr.create")).click();
      await expect(d.locator('[aria-invalid="true"]').first()).toBeVisible();
    } },
    // Owner testing 19 Oct 2026 (DT-210): two-step verification management on the profile.
    { name: "profile#tfa-off", route: "profile", open: header("profile.tfaTurnOff") },
    { name: "profile#tfa-off-errors", route: "profile", open: async (p, t) => {
      await header("profile.tfaTurnOff")(p, t);
      await p.locator(".dialog").getByRole("button", { name: new RegExp(t("profile.tfaTurnOff")) }).last().click();
      await expect(p.locator('.dialog [aria-invalid="true"]').first()).toBeVisible();
    } },
    { name: "profile#tfa-codes", route: "profile", open: header("profile.tfaNewCodes") },
    { name: "profile#tfa-recovery", route: "profile", open: async (p, t) => {
      await header("profile.tfaChangeApp")(p, t);
      await p.locator(".dialog .linkbtn").click();
    } },
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

/** A state of a screen reached by a few steps (sections opened, rows added): checked like a screen, no dialog expected. */
type State = { name: string; route: string; act: (page: Page, t: Text) => Promise<void> };

/** Report builder (owner feedback 13 Oct 2026, SCR-52): a data source with groupings, measures and filters added, and detail mode. */
const builderSource = async (p: Page, t: Text) => { await p.getByLabel(t("rb.source")).selectOption({ index: 1 }); };
const clickTimes = async (p: Page, text: string, times: number) => { for (let i = 0; i < times; i++) await button(p.locator("main"), text).click(); };

function companyStates(ids: Record<string, string | null>, w: World): State[] {
  void ids; void w;
  return [
    { name: "builder#groups-measures-filters", route: "reports/builder", act: async (p, t) => {
      await builderSource(p, t); await clickTimes(p, t("rb.addGroup"), 1); await clickTimes(p, t("rb.addMeasure"), 2); await clickTimes(p, t("rb.addFilter"), 3); } },
    { name: "builder#detail-filters", route: "reports/builder", act: async (p, t) => {
      await builderSource(p, t); await p.getByRole("switch", { name: t("rb.detail") }).click(); await clickTimes(p, t("rb.addFilter"), 2); } },
    // Owner feedback 13 Oct 2026: a detail result, then a summary with only a column grouping — stopped on the screen, the setting
    // marked, the earlier result dimmed under "not up to date".
    { name: "builder#not-up-to-date", route: "reports/builder", act: async (p, t) => {
      await builderSource(p, t); await p.getByRole("switch", { name: t("rb.detail") }).click();
      await p.locator("main fieldset input[type=checkbox]").first().check(); await button(p.locator("main"), t("rep.run")).click();
      await expect(p.locator("main table").first()).toBeVisible();
      await p.getByRole("switch", { name: t("rb.detail") }).click();
      await p.getByLabel(t("rb.field"), { exact: true }).first().selectOption({ index: 1 });
      await button(p.locator("main"), t("rep.run")).click();
      await expect(p.locator(".banner.err")).toBeVisible(); await expect(p.locator("main .stale")).toBeVisible(); } },
    // A refusal from the server (a text field compared with "greater than"): the message names "Filter 1" and the filter is marked.
    { name: "builder#server-refused", route: "reports/builder", act: async (p, t) => {
      await builderSource(p, t); await p.getByLabel(t("rb.groupN", 1), { exact: true }).selectOption({ index: 1 });
      await clickTimes(p, t("rb.addFilter"), 1);
      await p.getByLabel(t("rb.filterN", 1), { exact: true }).selectOption({ index: 1 });
      await p.getByLabel(t("rb.operator"), { exact: true }).selectOption({ index: 2 });
      await p.getByLabel(t("rb.value"), { exact: true }).fill("1");
      await button(p.locator("main"), t("rep.run")).click();
      await expect(p.locator(".banner.err")).toBeVisible(); await expect(p.locator("main [aria-invalid=true]").first()).toBeVisible(); } },
    // New journal entry (SCR-32): the account list of a line, open over the lines' scroll box.
    { name: "journal-new#account-list", route: "accounting/journal/new", act: async (p) => {
      const combo = p.locator("main table [role=combobox]").first(); await combo.click(); await combo.press("ArrowDown");
      await expect(p.locator("[role=listbox]").first()).toBeVisible(); } },
    // Field configuration (SCR-75): the Drivers tab (long form preview) and the Customers tab.
    { name: "fields#drivers", route: "settings/fields", act: async (p, t) => { await p.getByRole("tab", { name: t("nav.drivers") }).click(); await expect(p.locator(".sk")).toHaveCount(0); } },
    { name: "fields#customers", route: "settings/fields", act: async (p, t) => { await p.getByRole("tab", { name: t("nav.customers") }).click(); await expect(p.locator(".sk")).toHaveCount(0); } },
    // Customer statement (RPT-12) shown without its required customer: the field marked, one message.
    { name: "financial#customer-statement-required", route: "reports/financial?report=customer-statement", act: async (p, t) => {
      await button(p.locator("main"), t("rep.run")).click(); await expect(p.locator("main [aria-invalid=true]").first()).toBeVisible(); } },
    // Supplier statement (RPT-24, owner decision 17 Oct 2026): the visual supplier's bill, return and deduction columns.
    { name: "financial#supplier-statement", route: "reports/financial?report=supplier-statement", act: async (p, t) => {
      await pick(p.locator("main"), t("rep.supplier"), "Visual supplier"); await button(p.locator("main"), t("rep.run")).click();
      await expect(p.locator("main table tbody tr").nth(1)).toBeVisible(); } },
    // Readable history and audit log (owner feedback 14 Oct 2026, DT-188): a trip's history (created, added to an invoice, invoiced), a
    // journal entry's history (posted with its lines), the audit log as read by everyone and with an administrator's technical details.
    { name: "history#trip", route: `trips/${ids.invoicedTrip ?? ids.trip}`, act: async (p) => {
      await expect(p.locator(".hist .ev-t").first()).toBeVisible(); await p.locator(".hist").first().scrollIntoViewIfNeeded(); } },
    ...(ids.journal ? [{ name: "history#journal-entry", route: `accounting/journal/${ids.journal}`, act: async (p: Page) => {
      await expect(p.locator(".hist .ev-t").first()).toBeVisible(); await p.locator(".hist").first().scrollIntoViewIfNeeded(); } }] : []),
    { name: "audit-log#readable", route: "settings/audit-log", act: async (p) => { await expect(p.locator("main .ev-t").first()).toBeVisible(); } },
    { name: "audit-log#technical", route: "settings/audit-log", act: async (p, t) => {
      await button(p.locator("main"), t("aud.technical")).click(); await expect(p.locator("main .tech").first()).toBeVisible(); } },
    // Notifications (owner feedback 15 Oct 2026, DT-189): a rejected e-invoice and the single "not set up correctly" notification for
    // refused credentials in the panel; then the page a notification opens (the invoice, in the company named by its URL token).
    { name: "notifications#panel", route: "", act: async (p, t) => { await notificationsWorld(p, companyOf(p)); await openNotifications(p, t); } },
    { name: "notifications#opened", route: "", act: async (p, t) => {
      await notificationsWorld(p, companyOf(p));
      await openNotifications(p, t);
      await p.locator(".notes .note-open").filter({ hasText: /INV-\d/ }).first().click();
      await expect(p).toHaveURL(new RegExp(`/c/${companyOf(p)}/invoices/[A-Za-z0-9_-]+$`));
      await expect(p.locator(".panel-side")).toHaveCount(0);
      await expect(p.locator("main .sk")).toHaveCount(0);
      await expect(p.locator("main h1")).toContainText(/INV-\d/);
    } },
    // Company switch (owner feedback 14 Oct 2026, DT-187): from the dashboard of the company with documents to a second, empty company of
    // the tenant through the company menu — the dashboard shows the new company's figures, never the old one's, without a reload. Last:
    // it adds a company to the tenant.
    { name: "dashboard#company-switch", route: "", act: async (p, t) => {
      const first = companyOf(p);
      const name = `Switch ${Date.now() % 100000}`;
      const second = await api<{ id: string }>(p, "POST", "/api/v1/tenant/companies", { name, nameEn: name });
      await p.reload(); await waitForApp(p);   // the company menu lists the new company
      await expect(p.locator("main .sk")).toHaveCount(0);
      const before = await p.locator("main").innerText();
      const collections = before.match(/\d[\d,]*\.\d{3}/g) ?? [];
      expect(collections.length).toBeGreaterThan(0);   // the first company shows its figures
      await p.getByRole("button", { name: t("shell.switchCompany") }).click();
      await p.getByRole("menuitemradio", { name }).click();
      await expect(p).toHaveURL(new RegExp(`/c/${second.id}/?$`));
      await expect(p.locator("main .sk")).toHaveCount(0);
      await expect(p.locator("main")).toContainText(name);   // the crumb: the new company
      const after = await p.locator("main").innerText();
      for (const figure of collections.filter((f) => f !== "0.000")) expect(after, `figure ${figure} of ${first} after the switch`).not.toContain(figure);
    } },
  ];
}

const notified = new Set<string>();

/**
 * Notifications of the company (DT-189), once: the simulator refuses an invoice of a "[reject]" customer (one notification per document),
 * then refuses the credentials of two invoices (one notification for the company, two affected invoices).
 */
async function notificationsWorld(page: Page, company: string): Promise<void> {
  if (notified.has(company)) return;
  notified.add(company);
  const settings = async (clientId: string) => {
    const s = await api<{ rowVersion: string | null }>(page, "GET", c(company, "settings/e-invoicing"));
    await api(page, "PUT", c(company, "settings/e-invoicing"), { isEnabled: true, connector: "Simulator", sellerTaxNumber: "12345678",
      registrationType: "Sales", incomeSourceSequence: "4569874", clientId, secretKey: "secret-1", environment: "Test", rowVersion: s.rowVersion });
  };
  const customer = (name: string) => api<{ id: string }>(page, "POST", c(company, "customers"),
    { name: `${name} ${Date.now() % 100000}`, taxNumber: "200014587", phone: "0781234567", paymentTermsDays: 30 });
  const invoice = (customerId: string) => api(page, "POST", c(company, "invoices"), { customerId, invoiceType: "Manual", invoiceDate: today(),
    lines: [{ description: "Equipment move", quantity: 1, unitPrice: 100 }], discount: 0, extraFees: 0, approve: true });
  type Note = { type: string; message: string };
  const until = (ok: (items: Note[]) => boolean) => expect.poll(async () =>
    ok((await api<{ items: Note[] }>(page, "GET", c(company, "notifications"))).items), { timeout: 90_000, intervals: [1_000] }).toBe(true);
  await settings("client-1");
  await invoice((await customer("شركة الرفض [reject]")).id);
  await until((items) => items.some((n) => n.type === "EINV.Rejected"));
  await settings("client-[refuse]");
  const normal = await customer("شركة الأمانة");
  await invoice(normal.id);
  await invoice(normal.id);
  await until((items) => items.some((n) => n.type === "EINV.SetupError" && /\b2\b/.test(n.message)));
}

async function openNotifications(page: Page, t: Text): Promise<void> {
  await page.getByRole("button", { name: new RegExp(`^${t("shell.notifications", "").replace(/\s*\(\)$/, "")}`) }).click();
  await expect(page.locator(".notes .note").first()).toBeVisible();
}

async function sweepStates(page: Page, run: VisualRun, lang: Lang, company: string, states: State[]): Promise<void> {
  const t = texts(lang);
  page.setDefaultTimeout(15_000);   // a step that cannot be done fails the state quickly instead of waiting for the test's time limit
  for (const s of states) {
    const route = s.route ? `/c/${company}/${s.route}` : `/c/${company}`;
    if (!(await open(page, route))) { run.findings.push({ kind: "not-shown", text: "screen did not render", where: route, screen: s.name, lang, width: 0, density: "" }); continue; }
    try {
      await s.act(page, t);
    } catch (e) {
      run.findings.push({ kind: "not-shown", text: `state not reached: ${String(e).split("\n")[0].slice(0, 160)}`, where: route, screen: s.name, lang, width: 0, density: "" });
      continue;
    }
    await run.check(page, s.name, { full: true });
  }
}

async function sweepPopups(page: Page, run: VisualRun, lang: Lang, company: string | null, popups: Popup[]): Promise<void> {
  const t = texts(lang);
  page.setDefaultTimeout(15_000);   // an opener that cannot act (e.g. no row to click) fails that popup instead of waiting for the test's limit
  for (const p of popups.filter((x) => wanted(x.name, x.route))) {
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
    test.skip(!companyPopups({}, {} as World, "").some((p) => wanted(p.name, p.route)), "not among VISUAL_ONLY");
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
    test.skip(!["/console", "console", "/console/tenants", "/console/plans", "/console/announcements", "/console/support-access", "/console/staff", "/console/health"].some((r) => wanted(r)), "not among VISUAL_ONLY");
    test.setTimeout(30 * 60_000);
    const run = new VisualRun(lang, "console");
    await staffSignIn(page, lang);
    for (const route of ["/console/tenants", "/console/plans", "/console/announcements", "/console/support-access", "/console/staff", "/console/health", "/console/help/console-staff"]) {
      if (!wanted(route)) continue;
      if (await open(page, route)) await run.check(page, route.slice(1), { full: true });
      else run.findings.push({ kind: "not-shown", text: "screen did not render", where: route, screen: route, lang, width: 0, density: "" });
    }
    await sweepPopups(page, run, lang, null, [
      { name: "console-tenants#panel", route: "/console/tenants", open: firstRow },
      { name: "console-tenants#extend", route: "/console/tenants", optional: true, open: async (p, t) => {
        await firstRow(p); await button(p.locator(".panel-side"), t("con.extendTrial")).click(); } },
      // DT-183: the panel of a tenant whose checkout is unpaid (made by the screens test), with "Cancel invoice", and its dialog.
      { name: "console-tenants#awaiting-invoice", route: "/console/tenants", open: async (p, t) => { await awaitingPanel(p, t); } },
      { name: "console-tenants#cancel-invoice", route: "/console/tenants", open: async (p, t) => { await (await awaitingPanel(p, t)).click(); } },
      { name: "console-plans#new", route: "/console/plans", open: header("con.newPlan") },
      // DT-185: "Mark as refunded" on Platform health.
      { name: "console-health#mark-refunded", route: "/console/health", open: async (p, t) => {
        await p.locator("main table button").filter({ hasText: t("con.markRefunded") }).first().click(); } },
      { name: "console-announcements#new", route: "/console/announcements", open: header("con.newAnnouncement") },
      { name: "console-support#request", route: "/console/support-access", open: header("con.requestSupport") },
      // DT-214: the searchable subscriber field with its results, then a chosen subscriber shown again before sending (nothing is sent).
      { name: "console-support#results", route: "/console/support-access", open: async (p, t) => {
        await header("con.requestSupport")(p, t); await p.locator("#support-tenant").click(); await expect(p.getByRole("option").first()).toBeVisible(); } },
      { name: "console-support#chosen", route: "/console/support-access", open: async (p, t) => {
        await header("con.requestSupport")(p, t); await p.locator("#support-tenant").click(); await p.getByRole("option").first().click();
        await expect(p.locator("[data-testid=chosen-tenant]")).toBeVisible(); } },
    ]);
    run.assertClean();
  });
}

/**
 * Owner testing 20 Oct 2026 (DT-220): a staff member is on the subscriber's dashboard in a support session when the subscriber ends the
 * window. The staff page must show "ended by the subscriber at HH:MM" with "Back to provider console" — pushed live, without a new request —
 * with no support banner and no company data; a reload shows the same (the server answers 403 with the reason, never a server error).
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — support session ended (${lang})`, async ({ page }) => {
    test.skip(!wanted("support-ended"), "not among VISUAL_ONLY");
    test.setTimeout(10 * 60_000);
    const run = new VisualRun(lang, "support-ended");
    const t = texts(lang);
    const owner = await enrol(page, lang, new VisualRun(lang, "support-ended-enrol", false));
    const company = companyOf(page);
    const staff = await page.context().browser()!.newPage({ baseURL: BASE_URL, ignoreHTTPSErrors: true });
    try {
      await staffSignIn(staff, lang);
      const found = await api<{ id: string; ownerEmail: string | null }[]>(staff, "GET", `/api/v1/platform/support-access/tenants?q=${encodeURIComponent(owner.email)}`);
      await api(staff, "POST", "/api/v1/platform/support-access", { tenantId: found.find((x) => x.ownerEmail === owner.email)!.id, reason: "Visual check" });
      const grants = await api<{ id: string; status: string }[]>(page, "GET", "/api/v1/tenant/support-access");
      const grant = grants.find((g) => g.status === "Requested")!.id;
      await api(page, "POST", `/api/v1/tenant/support-access/${grant}/approve`, { hours: 1 });
      await staff.goto(`/c/${company}`);
      await expect(staff.locator(".banner").filter({ hasText: t("banner.support") })).toBeVisible({ timeout: 30_000 });
      // The page opens its live connection after it has loaded: give it a moment, as a person reading the dashboard would.
      await staff.waitForTimeout(3000);
      // The subscriber ends the window: the staff page changes by itself (live message).
      await api(page, "POST", `/api/v1/tenant/support-access/${grant}/end`);
      await expect(staff.locator("[data-testid=support-ended]")).toBeVisible({ timeout: 15_000 });
      await expect(staff.locator(".banner").filter({ hasText: t("banner.support") })).toHaveCount(0);
      await expect(staff.getByRole("link", { name: t("support.backToConsole") })).toBeVisible();
      await run.check(staff, "support-ended#live");
      // A reload asks the server again: 403 with the reason, the same screen.
      await staff.reload();
      await expect(staff.locator("[data-testid=support-ended]")).toBeVisible({ timeout: 30_000 });
      await expect(staff.getByText(t("ui.serverErrorTitle"))).toHaveCount(0);
      await run.check(staff, "support-ended#reload");
    } finally {
      await staff.close();
    }
    run.assertClean();
  });
}

/**
 * Owner testing 21 Oct 2026 (DT-226, DT-228, DT-229): the e-invoicing settings with income tax chosen (the 0% offer), the driver settlement
 * with the driver's repayment (the trips unticked: only the 20.000 advance is left, so the net is negative), the profile page with its
 * "Change email" dialog, and the Users & roles dialogs for name / username and for starting an email change. Openers write nothing.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — account changes and repayment (${lang})`, async ({ page }) => {
    test.skip(!wanted("einvoice#income", "settle#repayment", "profile#", "users#edit-account", "users#change-email"), "not among VISUAL_ONLY");
    test.setTimeout(20 * 60_000);
    const run = new VisualRun(lang, "account-changes");
    const t = texts(lang);
    await enrol(page, lang, new VisualRun(lang, "account-changes-enrol", false));
    const company = companyOf(page);
    const { w } = await documents(page, company);
    const roles = await api<{ id: string; name: string }[]>(page, "GET", c(company, "roles"));
    await api(page, "POST", c(company, "users"), { fullName: lang === "ar" ? "كاتب الحسابات" : "Accounts clerk", userName: `clerk.${Date.now()}`, email: null,
      password: "Visual-Passw0rd-1", role: roles.find((r) => r.name === "Accountant")!.id, mustChangePassword: false });
    const route = (r: string) => `/c/${company}/${r}`;

    expect(await open(page, route("settings/e-invoicing"))).toBe(true);
    const registration = page.locator("main select").filter({ has: page.locator("option", { hasText: t("eis.reg.Income") }) });
    await registration.selectOption({ label: t("eis.reg.Income") });
    await expect(page.getByText(t("eis.zeroOfferAccept"))).toBeVisible({ timeout: 10_000 });
    await run.check(page, "einvoice#income-offer");

    expect(await open(page, route(`driver-accounts/${w.driver}/settle`))).toBe(true);
    await page.locator("main table").first().locator("thead input[type=checkbox]").uncheck();
    await expect(page.getByRole("heading", { name: t("drv.repayTitle") })).toBeVisible({ timeout: 10_000 });
    await run.check(page, "settle#repayment");

    expect(await open(page, route("profile"))).toBe(true);
    await run.check(page, "profile#page");
    await button(page.locator("main"), t("profile.changeEmail")).click();
    await expect(page.locator(".dialog")).toBeVisible({ timeout: 10_000 });
    await run.check(page, "profile#change-email");

    expect(await open(page, route("settings/users"))).toBe(true);
    const clerkRow = page.locator("main table tbody tr").filter({ hasText: lang === "ar" ? "كاتب الحسابات" : "Accounts clerk" });
    await clerkRow.click();
    await button(page.locator(".panel-side"), t("usr.editAccount")).click();
    await expect(page.locator(".dialog")).toBeVisible({ timeout: 10_000 });
    await run.check(page, "users#edit-account");
    await page.keyboard.press("Escape");
    await expect(page.locator(".dialog")).toHaveCount(0, { timeout: 10_000 });
    await button(page.locator(".panel-side"), t("profile.changeEmail")).click();
    await expect(page.locator(".dialog")).toBeVisible({ timeout: 10_000 });
    await run.check(page, "users#change-email");
    run.assertClean();
  });
}

/**
 * Owner decision 21 Oct 2026 on DT-228 (DT-230): a partner trip already paid — the void asks what to do with the amount (warning, three
 * choices); then, after a void with "Deduct" and two newer trips, the next payment sets it off: the partner statement shows the trip voided
 * after payment and the deduction on that payment.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — paid partner trip (${lang})`, async ({ page }) => {
    test.skip(!wanted("partner-paid#"), "not among VISUAL_ONLY");
    test.setTimeout(15 * 60_000);
    const run = new VisualRun(lang, "partner-paid");
    const t = texts(lang);
    await enrol(page, lang, new VisualRun(lang, "partner-paid-enrol", false));
    const company = companyOf(page);
    const w = await buildWorld(page, company);
    const partner = await api<{ id: string }>(page, "POST", c(company, "partners"), { name: lang === "ar" ? "النور للنقل" : "Al-Noor Transport" });
    const rented = await api<{ id: string }>(page, "POST", c(company, "ownership-types"), { name: "Rented", fleetClass: "External" });
    const year = Number(today().slice(0, 4)) + 1;
    const driver = await api<{ id: string }>(page, "POST", c(company, "drivers"), {
      fullName: lang === "ar" ? "سامي العبادي" : "Sami Abbadi", nationalId: String(9_000_000_000 + Math.floor(Math.random() * 999_999_999)), phone1: "0791234567",
      licenceNo: "L-2", licenceType: "C", licenceExpiry: `${year}-01-01`, paymentMethod: "Cash", plateNumber: `10-${Math.floor(10000 + Math.random() * 89999)}`,
      truckType: "Volvo", capacityTons: 32, ownershipTypeId: rented.id, partnerId: partner.id, registrationExpiry: `${year}-01-01`, insuranceExpiry: `${year}-01-01`,
      inspectionExpiry: `${year}-01-01` });
    await api(page, "POST", c(company, "tariffs"), { partyType: "Partner", partyId: partner.id, price: 2.75, validFrom: `${today().slice(0, 7)}-01` });
    const trip = async (receiptNo: string) => (await api<{ trip: { id: string; partnerCost: number } }>(page, "POST", c(company, "trips"), {
      receiptNo, tripDate: today(), driverId: driver.id, customerId: w.customer, loadingSiteId: w.loading, unloadingSiteId: w.unloading, materialId: w.material,
      partnerId: partner.id, loadedWeight: 30, unloadedWeight: 30 })).trip;
    const accounts = await api<{ bankAccounts: { id: string }[]; cashBoxes: { id: string }[] }>(page, "GET", c(company, "accounting/money-accounts/options"));
    const cash = accounts.cashBoxes[0].id;
    const paid = await trip(`PP-1-${company.slice(0, 4)}`);
    await api(page, "POST", c(company, "partner-payments"), { partnerId: partner.id, paymentDate: today(), amount: paid.partnerCost, paymentMethod: "Cash", cashBoxId: cash });

    expect(await open(page, `/c/${company}/trips/${paid.id}`)).toBe(true);
    await button(page.locator("main"), t("trip.void")).click();
    await page.locator(".dialog textarea").fill(lang === "ar" ? "ألغى العميل الحمولة" : "The customer cancelled the load");
    await button(page.locator(".dialog"), t("trip.void")).click();
    await expect(page.locator(".dialog").getByText(t("trip.paidChoose"))).toBeVisible({ timeout: 15_000 });
    await run.check(page, "partner-paid#dialog");
    await page.locator(".dialog input[type=radio][value=NotPaid]").check();
    await run.check(page, "partner-paid#not-paid");

    // Deduct, two newer trips, the next payment sets it off.
    const current = await api<{ trip: { rowVersion: string } }>(page, "GET", c(company, `trips/${paid.id}`));
    await api(page, "POST", c(company, `trips/${paid.id}/void`), { reason: "Load cancelled", rowVersion: current.trip.rowVersion, partnerPaidHandling: "Deduct" });
    const t2 = await trip(`PP-2-${company.slice(0, 4)}`);
    const t3 = await trip(`PP-3-${company.slice(0, 4)}`);
    await api(page, "POST", c(company, "partner-payments"), { partnerId: partner.id, paymentDate: today(), amount: t2.partnerCost + t3.partnerCost - paid.partnerCost,
      paymentMethod: "Cash", cashBoxId: cash });
    expect(await open(page, `/c/${company}/partner-accounts/${partner.id}`)).toBe(true);
    await page.getByRole("tab", { name: t("md.statement") }).click();
    await expect(page.locator("main table").getByText(`PP-1-${company.slice(0, 4)}`).first()).toBeVisible({ timeout: 15_000 });
    await run.check(page, "partner-paid#statement");
    run.assertClean();
  });
}
