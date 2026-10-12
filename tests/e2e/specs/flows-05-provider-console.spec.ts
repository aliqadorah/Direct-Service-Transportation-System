import { expect, test } from "@playwright/test";
import { firstSignIn, signUp, type Lang } from "../support/auth";
import { staffSignIn } from "../support/staff";
import { texts } from "../support/text";
import { button, field, toast } from "../support/ui";

/**
 * Platform staff and a tenant Owner (PRD 5.5, D-13, BR-014), in Arabic RTL and English: the console lists the tenant with its plan and
 * usage; staff extend its trial with a reason and request support access; the Owner approves a window on the tenant screen; staff then
 * open the tenant's company read-only (support banner, no data entry).
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`5.5 / BR-014 Staff console, support request, Owner approval, read-only session (${lang})`, async ({ browser }) => {
    const t = texts(lang);
    const ownerPage = await (await browser.newContext()).newPage();
    const organisation = `Console ${lang} ${Date.now().toString(36)}`;
    await firstSignIn(ownerPage, await signUp(ownerPage, lang, organisation), lang);

    const staff = await (await browser.newContext()).newPage();
    await staffSignIn(staff, lang);
    await expect(staff).toHaveURL(/console\/tenants/);
    await staff.getByRole("searchbox").first().fill(organisation);
    await staff.getByRole("searchbox").first().press("Enter");
    await staff.getByRole("row").filter({ hasText: organisation }).click();

    const panel = staff.locator(".panel-side");
    await button(panel, t("con.extendTrial")).click();
    const dialog = staff.locator(".dialog");
    await dialog.locator("textarea").fill(lang === "ar" ? "تمديد بطلب العميل" : "Extended on request");
    await button(dialog, t("ui.confirm")).click();
    await expect(toast(staff, t("con.done.extend"))).toBeVisible();

    await button(panel, t("con.requestSupport")).click();
    await dialog.locator("textarea").fill(lang === "ar" ? "فاتورة مرفوضة إلكترونياً" : "Rejected e-invoice");
    await button(dialog, t("ui.confirm")).click();
    await expect(toast(staff, t("con.done.support"))).toBeVisible();

    // The Owner approves the window (24 h by default).
    await ownerPage.goto("/tenant/support-access");
    await button(ownerPage, t("ten.approve")).click();
    await ownerPage.locator(".dialog").getByRole("button", { name: t("ten.approve") }).last().click();
    await expect(ownerPage.getByRole("button", { name: t("ten.endNow") })).toBeVisible();

    // Staff open the company read-only.
    await staff.goto("/console/support-access");
    await staff.getByRole("link", { name: organisation }).click();
    await expect(staff.locator(".banner").filter({ hasText: t("banner.support") })).toBeVisible();
    await staff.goto(staff.url().replace(/\/c\/([^/]+).*$/, "/c/$1/trips"));
    await expect(staff).toHaveURL(/trips\/list/);
    await expect(field(staff, t("trip.receiptNo"))).toHaveCount(0);
  });
}
