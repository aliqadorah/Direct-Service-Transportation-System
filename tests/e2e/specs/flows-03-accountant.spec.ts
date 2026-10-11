import { expect, test } from "@playwright/test";
import { companyOf, firstSignIn, signUp, type Lang } from "../support/auth";
import { texts } from "../support/text";
import { button, choose, field, pick, toast } from "../support/ui";
import { addTrip, buildWorld, inviteMember } from "../support/world";

/**
 * Accountant (PRD 3.5), in Arabic RTL and English, on three trips recorded by dispatch:
 *  9.6 trip-based invoice — customer, unbilled trips with live totals, save as draft (approval on by default), approve → gap-free INV- number;
 *  9.9 customer receipt against the invoice from its screen → RCV- number, balance reduced;
 *  9.8 credit note on one trip of the approved invoice with a reason → CRN- number;
 *  9.10 driver advance → ADV- number, then the driver's settlement with the open trips and the advance → STL- number.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`9.6 / 9.8 / 9.9 / 9.10 Accountant invoices, collects, credits and settles (${lang})`, async ({ page }) => {
    const t = texts(lang);
    await firstSignIn(page, await signUp(page, lang, `Accounts ${lang}`), lang);
    const company = companyOf(page);
    const w = await buildWorld(page, company);
    for (const r of ["A-1", "A-2", "A-3"]) await addTrip(page, w, `${r}-${lang}`);
    await inviteMember(page, company, "Accountant", lang);

    // 9.6 Invoice a customer.
    await page.goto(`/c/${company}/invoices/new`);
    await pick(page, t("trip.customer"), w.customerName);
    await expect(page.getByText(`A-1-${lang}`)).toBeVisible();
    await button(page, t("ui.selectAll")).click();
    await button(page, t("inv.saveDraft")).click();
    await expect(page).toHaveURL(/\/invoices\/[^/]+$/);
    await button(page, t("inv.approve")).click();
    await expect(toast(page, "INV-00001")).toBeVisible();
    const invoiceUrl = page.url();

    // 9.9 Receipt against the invoice (customer and invoice filled from the invoice screen).
    await page.locator("main").getByRole("link", { name: t("nav.newReceipt"), exact: true }).first().click();
    await expect(page).toHaveURL(/receipts\/new/);
    await expect(field(page, t("inv.amount"))).not.toHaveValue("");   // the invoice's remaining balance is proposed first
    await field(page, t("inv.amount")).fill("100");
    await choose(page, t("cash.cashBox"));
    await button(page, t("ui.save")).click();
    await expect(toast(page, "RCV-")).toBeVisible();

    // 9.8 Credit note on one trip of the invoice.
    await page.goto(invoiceUrl);
    await page.locator("main").getByRole("link", { name: t("cn.new"), exact: true }).first().click();
    await expect(page).toHaveURL(/credit-notes\/new/);
    await page.locator("tbody input[type=checkbox]").first().check();
    await field(page, t("ui.reason")).fill(lang === "ar" ? "حمولة مرتجعة" : "Load returned");
    await button(page, t("cn.approve")).click();
    await expect(toast(page, "CRN-")).toBeVisible();

    // 9.10 Driver advance, then the settlement.
    await page.goto(`/c/${company}/driver-accounts/${w.driver}/advance`);
    await field(page, t("inv.amount")).fill("50");
    await choose(page, t("cash.cashBox"));
    await button(page, t("ui.save")).click();
    await expect(toast(page, "ADV-")).toBeVisible();

    await page.goto(`/c/${company}/driver-accounts/${w.driver}/settle`);
    await choose(page, t("cash.cashBox"));
    await button(page, t("drv.confirmSettlement")).click();
    await expect(toast(page, "STL-")).toBeVisible();
  });
}
