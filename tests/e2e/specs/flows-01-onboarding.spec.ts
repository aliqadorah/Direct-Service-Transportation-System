import { expect, test } from "@playwright/test";
import { companyOf, firstSignIn, signUp, type Lang } from "../support/auth";

/**
 * PRD 9.1 Sign-up & first-time setup (Owner), in Arabic RTL and English: sign-up, e-mail verification, mandatory two-step set-up with
 * recovery codes, the trial banner, the setup checklist with its eight steps, and the company profile saved from settings.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`9.1 Owner signs up, verifies, sets up 2FA and starts setup (${lang})`, async ({ page }) => {
    const account = await signUp(page, lang, lang === "ar" ? "شركة النقل للاختبار" : "E2E Haulage Co");
    await firstSignIn(page, account, lang);

    await expect(page.locator("html")).toHaveAttribute("dir", lang === "ar" ? "rtl" : "ltr");
    await expect(page.locator(".banner").filter({ hasText: lang === "ar" ? "الفترة التجريبية" : "trial" })).toBeVisible();

    const company = companyOf(page);
    await page.goto(`/c/${company}/setup`);
    await expect(page.locator(".step, [data-step]").first()).toBeVisible();
    await expect(page.locator(".steps .step")).toHaveCount(8);

    await page.goto(`/c/${company}/settings/company`);
    const taxNumber = page.getByRole("textbox", { name: lang === "ar" ? "الرقم الضريبي" : "Tax number", exact: true });
    await taxNumber.fill("012345678");
    await page.getByRole("button", { name: lang === "ar" ? "حفظ" : "Save" }).first().click();
    await expect(page.locator(".toast")).toBeVisible();
  });
}
