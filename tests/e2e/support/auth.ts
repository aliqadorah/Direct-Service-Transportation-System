import { expect, type Page } from "@playwright/test";
import { PASSWORD } from "./env";
import { linkFromMail } from "./mail";
import { totp } from "./totp";

export type Lang = "ar" | "en";

export interface Account {
  email: string;
  password: string;
  key: string;
}

let seq = 0;
export const uniqueEmail = (who: string) => `${who}-${Date.now().toString(36)}${(seq++).toString(36)}@e2e.test`;

/** Submits the static server-rendered form with this FormName (Blazor adds a hidden `_handler` field to each form). */
export async function submitForm(page: Page, formName: string): Promise<void> {
  await page.locator(`form:has(input[name="_handler"][value="${formName}"]) button[type="submit"]`).click();
}

/** PRD 9.1 steps 1–2: sign-up, e-mail verification; returns the account (2FA is set up at the first sign-in). */
export async function signUp(page: Page, lang: Lang, organisation: string, fullName = "مالك الاختبار"): Promise<Account> {
  const email = uniqueEmail("owner");
  await page.goto(`/sign-up?lang=${lang}`);
  await page.locator('[name="Input.FullName"]').fill(fullName);
  await page.locator('[name="Input.OrganisationName"]').fill(organisation);
  await page.locator('[name="Input.Email"]').fill(email);
  await page.locator('[name="Input.Password"]').fill(PASSWORD);
  await page.locator('[name="Input.AcceptTerms"]').check();
  await submitForm(page, "sign-up");
  await verifyEmail(page, email);
  return { email, password: PASSWORD, key: "" };
}

export async function verifyEmail(page: Page, email: string): Promise<void> {
  await page.goto(await linkFromMail(email, "VerifyEmail"));
  await submitForm(page, "verify");
  await expect(page.locator('a.btn.pri[href*="/sign-in"]')).toBeVisible();
}

/** First sign-in: password, then the mandatory two-step set-up (SCR-05) with the shown key, recovery codes, continue into the app. */
export async function firstSignIn(page: Page, account: Account, lang: Lang): Promise<Account> {
  await passwordStep(page, account, lang);
  await expect(page).toHaveURL(/two-factor\/setup/);
  const key = ((await page.locator('form .num[dir="ltr"]').first().textContent()) ?? "").replace(/\s/g, "");
  await page.locator('[name="Input.Code"]').fill(totp(key));
  await submitForm(page, "two-factor-setup");
  await expect(page.locator('ul.num[dir="ltr"] li')).toHaveCount(10);   // ten recovery codes, shown once
  await page.locator("a.btn.pri.w100").click();
  await waitForApp(page);
  await useLanguage(page, lang);
  return { ...account, key };
}

/** Saves the interface language in the profile (FR-178) when it differs, and reloads into it. */
export async function useLanguage(page: Page, lang: Lang): Promise<void> {
  const me = await (await page.request.get("/api/v1/me")).json();
  if (me.languageCode === lang) return;
  const af = await (await page.request.get("/api/v1/auth/antiforgery")).json();
  const res = await page.request.put("/api/v1/me/preferences", {
    data: { languageCode: lang, defaultCompany: me.defaultCompany, dateFormat: me.dateFormat }, headers: { [af.headerName]: af.token },
  });
  if (!res.ok()) throw new Error(`preferences → ${res.status()}`);
  await page.reload();
  await waitForApp(page);
}

/** Sign-in of an enrolled user: password, then the authenticator code. */
export async function signIn(page: Page, account: Account, lang: Lang): Promise<void> {
  await passwordStep(page, account, lang);
  await expect(page).toHaveURL(/two-factor/);
  await page.locator('[name="Input.Code"]').fill(totp(account.key));
  await submitForm(page, "two-factor");
  await waitForApp(page);
  await useLanguage(page, lang);
}

async function passwordStep(page: Page, account: Account, lang: Lang): Promise<void> {
  await page.context().clearCookies();
  await page.goto(`/sign-in?lang=${lang}`);
  await page.locator('[name="Input.Email"]').fill(account.email);
  await page.locator('[name="Input.Password"]').fill(account.password);
  await submitForm(page, "sign-in");
}

/** The WebAssembly application has started and shows a screen title. */
export async function waitForApp(page: Page): Promise<void> {
  await expect(page).toHaveURL(/\/(c|console)\//, { timeout: 60_000 });
  await expect(page.locator("main h1").first()).toBeVisible({ timeout: 60_000 });
}

/** The company token of the current /c/{company}/… address. */
export function companyOf(page: Page): string {
  const m = /\/c\/([^/?#]+)/.exec(page.url());
  if (!m) throw new Error(`Not on a company page: ${page.url()}`);
  return decodeURIComponent(m[1]);
}
