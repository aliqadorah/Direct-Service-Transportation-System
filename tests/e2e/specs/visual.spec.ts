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
 * 1366 px wide, in the light and dark themes. Fails on overlapping text, text cut off by its box and sideways page scroll
 * (support/layout.ts). Screenshots of every combination go to TestResults/visual/<lang>/<width>-<theme>/ (VISUAL_SHOTS=0 skips them).
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
  // Owner request 25 Oct 2026 (DT-254, DT-255): a draft with line references and VAT 16 % on the expense line (tax account: input VAT 1105),
  // so the journal form shows taxed lines, their totals and the automatic tax line it will post.
  const taxCodes = await api<{ id: string; code: string }[]>(page, "GET", c(company, "accounting/tax-codes/options"));
  const vat16 = taxCodes.find((x) => x.code === "VAT_JO_16")!.id;
  const taxDraft = await api<{ id: string }>(page, "POST", c(company, "accounting/journal-drafts"), {
    entryDate: today(), description: "Office supplies / قرطاسية", reference: "INV-2210", lines: [
      { accountId: code("5299"), debit: 100, credit: 0, costCentreId: null, party: null, note: null, reference: "INV-2210", taxCodeId: vat16, taxAccountId: code("1105") },
      { accountId: code("110101"), debit: 0, credit: 116, costCentreId: null, party: null, note: null, reference: "PAY-77", taxCodeId: null, taxAccountId: null },
    ] });
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
      taxDraft: taxDraft.id,
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
    "invoices", "invoices/new", "invoices/manual", `invoices/${ids.invoice}`, `print/invoice/${ids.invoice}`, "credit-notes", `credit-notes/new?invoice=${ids.invoice}`, "e-invoicing",
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
    "settings/company", "settings/mapping", "settings/numbering", "settings/tax-codes", "settings/e-invoicing", "settings/invoice-layout", "settings/fields", "settings/users",
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
      const ids = { trip: "t", invoice: "i", receipt: "r", journal: "j", advance: "a", draft: "d", taxDraft: "td", statement: "s", bill: "b", billReturn: "br" };
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
        run.findings.push({ kind: "not-shown", text: "screen did not render", where: route, screen: route, lang, width: 0, theme: "" });
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
    // Owner request 25 Oct 2026 (DT-257): Settings → Tax codes and its edit dialog.
    { name: "tax-codes#edit", route: "settings/tax-codes", open: firstRow },
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
    // Owner request 25 Oct 2026 (DT-254, DT-255): a journal entry with taxed lines, with the tax code list and the tax account list open.
    ...(ids.taxDraft ? [
      { name: "journal-tax#lines", route: `accounting/journal/drafts/${ids.taxDraft}`, act: async (p: Page) => {
        await expect(p.locator("main table tr.auto").first()).toBeVisible(); } },
      { name: "journal-tax#tax-code-list", route: `accounting/journal/drafts/${ids.taxDraft}`, act: async (p: Page, t: Text) => {
        const combo = p.getByLabel(t("je.lineTaxCode", 1), { exact: true }); await combo.click(); await combo.press("ArrowDown");
        await expect(p.locator("[role=listbox]").first()).toBeVisible(); } },
      { name: "journal-tax#tax-account-list", route: `accounting/journal/drafts/${ids.taxDraft}`, act: async (p: Page, t: Text) => {
        const combo = p.getByLabel(t("je.lineTaxAccount", 1), { exact: true }); await combo.click(); await combo.press("ArrowDown");
        await expect(p.locator("[role=listbox]").first()).toBeVisible(); } },
    ] : []),
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
    if (!(await open(page, route))) { run.findings.push({ kind: "not-shown", text: "screen did not render", where: route, screen: s.name, lang, width: 0, theme: "" }); continue; }
    try {
      await s.act(page, t);
    } catch (e) {
      run.findings.push({ kind: "not-shown", text: `state not reached: ${String(e).split("\n")[0].slice(0, 160)}`, where: route, screen: s.name, lang, width: 0, theme: "" });
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
    const fail = (why: string) => run.findings.push({ kind: "not-shown", text: `not opened: ${why}`, where: route, screen: p.name, lang, width: 0, theme: "" });
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
      else run.findings.push({ kind: "not-shown", text: "screen did not render", where: route, screen: route, lang, width: 0, theme: "" });
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

/**
 * Owner testing 21 Oct 2026 (DT-232…DT-234): the profile's buttons wrap inside their card; Ctrl+S saves the trip being edited with the field
 * just typed; the Banks & cash boxes lists (a long IBAN) stay inside their cards and the statement panel goes under them when narrow — the
 * same for Cost centres and Maintenance.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — layout fixes of 21 Oct (${lang})`, async ({ page }) => {
    test.skip(!wanted("fix21#"), "not among VISUAL_ONLY");
    test.setTimeout(15 * 60_000);
    const run = new VisualRun(lang, "fix21");
    const t = texts(lang);
    await enrol(page, lang, new VisualRun(lang, "fix21-enrol", false));
    const company = companyOf(page);
    const w = await buildWorld(page, company);
    const chart = await api<{ id: string; code: string }[]>(page, "GET", c(company, "accounting/accounts"));
    await api(page, "POST", c(company, "accounting/bank-accounts"), { name: lang === "ar" ? "الحساب الجاري الرئيسي" : "Main current account",
      bankName: lang === "ar" ? "بنك القاهرة عمان" : "Cairo Amman Bank", accountNumber: "0131000302", iban: "JO94CBJO0010000000000131000302", currencyCode: "JOD",
      accountId: chart.find((x) => x.code === "110201")!.id });

    expect(await open(page, `/c/${company}/profile`)).toBe(true);
    await run.check(page, "fix21#profile");
    await page.locator(".acct-actions").scrollIntoViewIfNeeded();
    await run.check(page, "fix21#profile-actions");
    await page.locator(".formbar.start").last().scrollIntoViewIfNeeded();
    await run.check(page, "fix21#profile-2fa");
    for (const route of ["accounting/cash-banks", "accounting/cost-centres", "accounting/maintenance"]) {
      expect(await open(page, `/c/${company}/${route}`)).toBe(true);
      await run.check(page, `fix21#${route.split("/")[1]}`);
    }

    // Ctrl+S on a trip being edited, the field just typed and not left: saved with that value.
    const trip = await addTrip(page, w, `KS-1-${company.slice(0, 4)}`);
    expect(await open(page, `/c/${company}/trips/${trip}`)).toBe(true);
    const notes = page.getByLabel(t("md.notes"));
    await notes.fill("Ctrl+S " + lang);
    await page.keyboard.press("Control+KeyS");
    await expect(page.locator(".toast")).toBeVisible({ timeout: 15_000 });
    const saved = await api<{ trip: { notes: string | null } }>(page, "GET", c(company, `trips/${trip}`));
    expect(saved.trip.notes).toBe("Ctrl+S " + lang);
    await run.check(page, "fix21#trip-saved");
    run.assertClean();
  });
}

/**
 * Dashboards and reports of 22 Oct (DT-247…DT-249): the accounting dashboard and the main dashboard flow with one page scrollbar (no area of
 * their own that scrolls, nothing cut off); a report named above its filters, marked in the list, in the address and the tab's name; the
 * tariff-breach "Side" box shows Customers.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — dashboards and reports of 22 Oct (${lang})`, async ({ page }) => {
    test.skip(!wanted("rep22#"), "not among VISUAL_ONLY");
    test.setTimeout(15 * 60_000);
    const run = new VisualRun(lang, "rep22");
    const t = texts(lang);
    await enrol(page, lang, new VisualRun(lang, "rep22-enrol", false));
    const company = companyOf(page);
    await documents(page, company);
    /** Elements inside the page that scroll up and down by themselves (the page is the only vertical scroller of these screens). */
    const ownScrollers = () => page.evaluate(() => [...document.querySelectorAll("main.content *")].filter((e) => {
      const s = getComputedStyle(e);
      return /(auto|scroll)/.test(s.overflowY) && e.scrollHeight > e.clientHeight + 1;
    }).map((e) => `${e.tagName.toLowerCase()}.${[...e.classList].join(".")}`));

    for (const route of ["accounting/dashboard", ""]) {
      expect(await open(page, `/c/${company}${route ? `/${route}` : ""}`)).toBe(true);
      for (const width of [1366, 1920]) {
        await page.setViewportSize({ width, height: 680 });
        expect(await ownScrollers(), `${route || "dashboard"} at ${width}`).toEqual([]);
      }
      await run.check(page, `rep22#${route ? "accounting-dashboard" : "dashboard"}`, { full: true });
    }

    expect(await open(page, `/c/${company}/reports/operational`)).toBe(true);
    await page.locator(".rep-list button", { hasText: "RPT-04" }).click();
    await expect(page.locator("#rep-head h2")).toContainText("RPT-04", { timeout: 15_000 });
    await expect(page).toHaveURL(/report=tariff-breach.*side=customer/);
    await expect(page).toHaveTitle(/RPT-04/);
    await expect(page.getByLabel(t("rep.side")).locator("option:checked")).toHaveText(t("trip.customer"));
    await run.check(page, "rep22#tariff-breach");
    await page.reload();
    await expect(page.locator(".rep-list button[aria-current=page]")).toContainText("RPT-04", { timeout: 30_000 });

    expect(await open(page, `/c/${company}/reports/financial?report=driver-balances`)).toBe(true);
    await expect(page.locator("#rep-head h2")).toContainText("RPT-19", { timeout: 15_000 });
    await expect(page.getByLabel(t("rep.asOf"))).not.toHaveValue("");
    await run.check(page, "rep22#driver-balances");
    run.assertClean();
  });

  /**
   * RPT-23 of 25 Oct (DT-262): manual entries on 110301 and 110302 — the control accounts match their parties, so an amber note (not the red
   * banner), the help for every figure, the documents figures on every account, and the entries behind a documents difference opened.
   */
  test(`Visual — RPT-23 reconciliation (${lang})`, async ({ page }) => {
    test.skip(!wanted("rpt23#"), "not among VISUAL_ONLY");
    test.setTimeout(15 * 60_000);
    const run = new VisualRun(lang, "rpt23");
    await enrol(page, lang, new VisualRun(lang, "rpt23-enrol", false));
    const company = companyOf(page);
    const { w } = await documents(page, company);
    const chart = await api<{ id: string; code: string }[]>(page, "GET", c(company, "accounting/accounts"));
    const code = (x: string) => chart.find((a) => a.code === x)!.id;
    const party = { type: "Customer", id: w.customer };
    await api(page, "POST", c(company, "accounting/journal-entries"), { entryDate: today(), description: lang === "ar" ? "رصيد قديم" : "Old balance", lines: [
      { accountId: code("110301"), debit: 200, credit: 0, costCentreId: null, party, note: null },
      { accountId: code("4101"), debit: 0, credit: 200, costCentreId: null, party: null, note: null }] });
    await api(page, "POST", c(company, "accounting/journal-entries"), { entryDate: today(), description: lang === "ar" ? "دفعة مقدمة من عميل" : "Advance from customer", lines: [
      { accountId: code("110101"), debit: 200, credit: 0, costCentreId: null, party: null, note: null },
      { accountId: code("110302"), debit: 0, credit: 200, costCentreId: null, party, note: null }] });
    expect(await open(page, `/c/${company}/reports/financial?report=reconciliation`)).toBe(true);
    await expect(page.locator("main .banner").first()).toBeVisible({ timeout: 15_000 });
    await expect(page.locator("main .banner.err")).toHaveCount(0);
    await run.check(page, "rpt23#report", { full: true });
    await page.locator("main button.linkbtn").first().click();
    await expect(page.locator("main table a").first()).toBeVisible();
    await run.check(page, "rpt23#entries", { full: true });
    run.assertClean();
  });

  /**
   * Budget and profit and loss of 26 Oct (DT-265, DT-266): a draft budget with every account at once (headings with subtotals, sticky header,
   * first column and totals; two rows ticked), and the profit and loss of this year with columns by cost centre (with "No cost centre") and
   * by month (twelve columns scroll inside the card).
   */
  test(`Visual — budget grid and profit and loss (${lang})`, async ({ page }) => {
    test.skip(!wanted("bpl26#"), "not among VISUAL_ONLY");
    test.setTimeout(15 * 60_000);
    const t = texts(lang);
    const run = new VisualRun(lang, "bpl26");
    await enrol(page, lang, new VisualRun(lang, "bpl26-enrol", false));
    const company = companyOf(page);
    await documents(page, company);
    const chart = await api<{ id: string; code: string }[]>(page, "GET", c(company, "accounting/accounts"));
    const code = (x: string) => chart.find((a) => a.code === x)!.id;
    const centre = await api<{ id: string }>(page, "POST", c(company, "accounting/cost-centres"), { code: "BR-N", name: lang === "ar" ? "فرع الشمال" : "North branch", rowVersion: null });
    const year = Number(today().slice(0, 4));
    const entry = async (date: string, debit: string, credit: string, amount: number, costCentreId: string | null) =>
      api(page, "POST", c(company, "accounting/journal-entries"), { entryDate: date, description: lang === "ar" ? "حركة" : "Movement", lines: [
        { accountId: code(debit), debit: amount, credit: 0, costCentreId, party: null, note: null },
        { accountId: code(credit), debit: 0, credit: amount, costCentreId, party: null, note: null }] });
    await entry(`${year}-01-15`, "110101", "4101", 2500, centre.id);
    await entry(`${year}-01-20`, "5101", "110101", 900, centre.id);
    await entry(`${year}-02-10`, "110101", "4102", 400, null);
    await entry(`${year}-02-12`, "5202", "110101", 300, null);
    const every = (v: number) => Array.from({ length: 12 }, () => v);
    let budget = await api<{ id: string; rowVersion: string; name: string }>(page, "POST", c(company, "accounting/budgets"), { fiscalYear: year, name: `Draft ${year} / مسودة` });
    budget = await api<{ id: string; rowVersion: string; name: string }>(page, "PUT", c(company, `accounting/budgets/${budget.id}`), { name: budget.name, rowVersion: budget.rowVersion, lines: [
      { accountId: code("4101"), costCentreId: null, months: every(2500) },
      { accountId: code("5101"), costCentreId: null, months: every(900) },
      { accountId: code("5202"), costCentreId: null, months: every(300) },
    ] });

    expect(await open(page, `/c/${company}/accounting/budgets/${budget.id}`)).toBe(true);
    await expect(page.locator("table.bud-grid input[data-c]").first()).toBeVisible({ timeout: 15_000 });
    await run.check(page, "bpl26#budget", { full: true });
    await page.getByLabel(t("bud.selectRow", "5202")).check();
    await page.getByLabel(t("bud.selectRow", "5203")).check();
    await expect(page.locator("table.bud-grid tr.sel")).toHaveCount(2);
    await run.check(page, "bpl26#budget-selected");

    expect(await open(page, `/c/${company}/accounting/profit-loss`)).toBe(true);
    await page.getByRole("button", { name: t("pl.q.thisYear") }).click();
    await page.getByLabel(t("pl.columnsBy")).selectOption({ label: t("pl.by.costCentre") });
    await page.getByRole("button", { name: t("rep.run"), exact: true }).click();
    await expect(page.locator("table.pl-grid thead")).toContainText(t("pl.noCostCentre"), { timeout: 15_000 });
    await run.check(page, "bpl26#pl-centre", { full: true });
    await page.getByLabel(t("pl.columnsBy")).selectOption({ label: t("pl.by.month") });
    await page.getByRole("button", { name: t("rep.run"), exact: true }).click();
    await expect(page.locator("table.pl-grid thead th")).toHaveCount(14, { timeout: 15_000 });   // item, 12 months, total
    await run.check(page, "bpl26#pl-month", { full: true });
    run.assertClean();
  });

  /**
   * Owner findings of 27 Oct (DT-268…DT-270): the budget with the grid (top card at full height, one page scrollbar) and with a filter that
   * matches no account (filters in one row, the message in the grid's card); RPT-19 with drivers who have trips, an advance and a settled
   * account; Tenant → Companies → Sign-in security with the corrected texts.
   */
  test(`Visual — fixes of 27 Oct (${lang})`, async ({ page }) => {
    test.skip(!wanted("fix27#"), "not among VISUAL_ONLY");
    test.setTimeout(15 * 60_000);
    const t = texts(lang);
    const run = new VisualRun(lang, "fix27");
    await enrol(page, lang, new VisualRun(lang, "fix27-enrol", false));
    const company = companyOf(page);
    const { w } = await documents(page, company);
    const chart = await api<{ id: string; code: string }[]>(page, "GET", c(company, "accounting/accounts"));
    const code = (x: string) => chart.find((a) => a.code === x)!.id;
    const year = Number(today().slice(0, 4));
    // A driver advance by a manual entry naming the driver (as on the demo company's 1104).
    await api(page, "POST", c(company, "accounting/journal-entries"), { entryDate: today(), description: lang === "ar" ? "سلفة افتتاحية" : "Opening advance", lines: [
      { accountId: code("1104"), debit: 100, credit: 0, costCentreId: null, party: { type: "Driver", id: w.driver }, note: null },
      { accountId: code("110101"), debit: 0, credit: 100, costCentreId: null, party: null, note: null }] });
    await api(page, "POST", c(company, "accounting/cost-centres"), { code: "BR-S", name: lang === "ar" ? "فرع الجنوب" : "South branch", rowVersion: null });
    const budget = await api<{ id: string }>(page, "POST", c(company, "accounting/budgets"), { fiscalYear: year, name: `Draft ${year} / مسودة` });

    expect(await open(page, `/c/${company}/accounting/budgets/${budget.id}`)).toBe(true);
    await expect(page.locator("table.bud-grid input[data-c]").first()).toBeVisible({ timeout: 15_000 });
    await run.check(page, "fix27#budget-grid", { full: true });
    await page.getByLabel(t("je.costCentre")).selectOption({ index: 1 });   // the new cost centre: no postings yet → no account
    await expect(page.locator("section.bud-box .empty")).toBeVisible({ timeout: 15_000 });
    await run.check(page, "fix27#budget-empty", { full: true });

    expect(await open(page, `/c/${company}/reports/financial?report=driver-balances`)).toBe(true);
    await expect(page.locator("#rep-head h2")).toContainText("RPT-19", { timeout: 15_000 });
    await expect(page.locator("main table tbody tr").first()).toBeVisible({ timeout: 15_000 });
    await run.check(page, "fix27#rpt19", { full: true });

    expect(await open(page, "/tenant/companies")).toBe(true);
    await page.getByRole("tab", { name: t("ten.security") }).click();
    await expect(page.getByText(t("ten.securityHelp"))).toBeVisible({ timeout: 15_000 });
    await run.check(page, "fix27#sign-in-security", { full: true });
    run.assertClean();
  });
}

/**
 * System email of 22 Oct (DT-250…DT-252): the console screen as it opens (development folder), with Microsoft 365 chosen (the write-only
 * secret field, help under every field), the fresh-code dialog of Save (nothing saved), and a test email (the development folder: nothing
 * leaves the machine) with its result and the delivery log.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — system email (${lang})`, async ({ page }) => {
    test.skip(!wanted("mail22#"), "not among VISUAL_ONLY");
    test.setTimeout(10 * 60_000);
    const run = new VisualRun(lang, "mail22");
    const t = texts(lang);
    await staffSignIn(page, lang);
    expect(await open(page, "/console/system-email")).toBe(true);
    await expect(page.locator("main h2", { hasText: t("sem.log") })).toBeVisible({ timeout: 15_000 });
    await run.check(page, "mail22#opened", { full: true });

    await page.locator("input[type=radio][value=Microsoft365]").check();
    await expect(page.locator("input[type=password]")).toBeVisible({ timeout: 15_000 });
    await page.getByLabel(t("sem.tenantId")).fill("11111111-2222-3333-4444-555555555555");
    await page.getByLabel(t("sem.clientId")).fill("66666666-7777-8888-9999-000000000000");
    await page.getByLabel(t("sem.mailbox")).fill("no-reply@directservicetech.com");
    await run.check(page, "mail22#microsoft365", { full: true });

    await page.locator("input[type=password]").fill("visual-check-not-a-secret");
    await button(page.locator("#sem-form"), t("sem.save")).click();
    if (await page.locator("#sem-code-input").isVisible({ timeout: 5_000 }).catch(() => false)) {
      await run.check(page, "mail22#code-dialog");
      await page.keyboard.press("Escape");
    }

    // Back to the settings in use (development folder) and a test email: written to the host's mail folder, never sent.
    await page.reload();
    await expect(page.locator("#sem-test")).toBeVisible({ timeout: 30_000 });
    await page.locator("#sem-test").fill(`visual-${lang}@e2e.test`);
    await button(page.locator("main"), t("sem.sendTest")).click();
    await expect(page.locator("[role=status] .banner.ok")).toBeVisible({ timeout: 15_000 });
    await expect(page.locator("main table.grid tbody tr").first()).toBeVisible({ timeout: 15_000 });
    await run.check(page, "mail22#tested", { full: true });
    run.assertClean();
  });
}

/**
 * Side menu of 22 Oct (DT-235…DT-239): expanded with icons on every entry and the labelled parts of Accounting; collapsed to the icon rail;
 * a section's entries opened with the keyboard beside the rail; Ctrl+K finding a menu page by its name in the other language.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — side menu (${lang})`, async ({ page }) => {
    test.skip(!wanted("nav22#"), "not among VISUAL_ONLY");
    test.setTimeout(10 * 60_000);
    const run = new VisualRun(lang, "nav22");
    const t = texts(lang);
    await enrol(page, lang, new VisualRun(lang, "nav22-enrol", false));
    const company = companyOf(page);

    expect(await open(page, `/c/${company}/accounting/trial-balance`)).toBe(true);
    await expect(page.locator(".side .sec.open .part").first()).toBeVisible({ timeout: 15_000 });
    await run.check(page, "nav22#expanded");

    await page.locator(".side button.collapse").click();
    await expect(page.locator("aside.side.mini")).toBeVisible({ timeout: 15_000 });
    await run.check(page, "nav22#collapsed");

    await page.locator(".side button.g", { hasText: t("nav.accounting") }).focus();
    await page.keyboard.press("Enter");
    await expect(page.locator(".side .sec.flown .fly a.it").first()).toBeFocused({ timeout: 15_000 });
    await run.check(page, "nav22#flyout");
    await page.keyboard.press("Escape");
    await expect(page.locator(".side .sec.flown")).toHaveCount(0, { timeout: 15_000 });

    // The collapsed state is kept for this user on this device.
    await page.reload();
    await expect(page.locator("aside.side.mini")).toBeVisible({ timeout: 30_000 });
    await page.locator(".side button.collapse").click();
    await expect(page.locator("aside.side.mini")).toHaveCount(0, { timeout: 15_000 });

    await page.keyboard.press("Control+KeyK");
    await page.locator("input[role=combobox]").fill(lang === "ar" ? "Trial balance" : "ميزان المراجعة");
    await expect(page.locator("li[role=option]", { hasText: t("nav.trialBalance") })).toBeVisible({ timeout: 15_000 });
    await run.check(page, "nav22#search");
    run.assertClean();
  });
}

/**
 * Maintenance zone of 22 Oct (DT-240): "Delete selected entries" with nothing ticked shows a hint (no technical message); with an entry
 * ticked the preview with the confirmation fields; another action's preview.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`Visual — maintenance zone (${lang})`, async ({ page }) => {
    test.skip(!wanted("mnt22#"), "not among VISUAL_ONLY");
    test.setTimeout(10 * 60_000);
    const run = new VisualRun(lang, "mnt22");
    const t = texts(lang);
    await enrol(page, lang, new VisualRun(lang, "mnt22-enrol", false));
    const company = companyOf(page);
    await buildWorld(page, company);   // chart of accounts, periods and posting set-up
    const chart = await api<{ id: string; code: string; isPostable: boolean; isControlAccount: boolean; accountType: string }[]>(page, "GET", c(company, "accounting/accounts"));
    const postable = chart.filter((x) => x.isPostable && !x.isControlAccount);
    const expense = postable.find((x) => /expens/i.test(x.accountType)) ?? postable[postable.length - 1];
    const cash = postable.find((x) => /asset/i.test(x.accountType)) ?? postable[0];
    const day = new Date(Date.now() - 86_400_000).toISOString().slice(0, 10);
    await api(page, "POST", c(company, "accounting/journal-entries"), { entryDate: day, description: lang === "ar" ? "إنترنت أكتوبر" : "Internet October",
      lines: [{ accountId: expense.id, debit: 50, credit: 0 }, { accountId: cash.id, debit: 0, credit: 50 }] });

    expect(await open(page, `/c/${company}/accounting/maintenance`)).toBe(true);
    await page.locator("input[name=mnt-action]").first().check();
    await expect(page.locator(".mnt-hint")).toHaveText(t("mnt.pickHint"), { timeout: 15_000 });
    await expect(page.getByText("entryIds")).toHaveCount(0);
    await run.check(page, "mnt22#nothing-ticked");

    await page.locator("tbody input[type=checkbox]").first().check();
    await expect(page.locator("#mnt-word")).toBeVisible({ timeout: 15_000 });
    await run.check(page, "mnt22#ticked");

    await page.locator("input[name=mnt-action]").nth(1).check();
    await expect(page.locator("#mnt-word")).toBeVisible({ timeout: 15_000 });
    await run.check(page, "mnt22#delete-all");
    run.assertClean();
  });
}
