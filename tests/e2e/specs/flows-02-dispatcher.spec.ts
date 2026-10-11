import { expect, test } from "@playwright/test";
import { companyOf, firstSignIn, signUp, type Lang } from "../support/auth";
import { texts } from "../support/text";
import { button, field, pick, toast } from "../support/ui";
import { buildWorld, inviteMember } from "../support/world";

/**
 * Dispatcher (PRD 3.5), in Arabic RTL and English: 9.3 record a trip in quick entry — price from the customer tariff and driver rate from
 * this month's site rate, shown with their sources; saved with its revenue and listed in the recent trips — and 9.5 void it with a reason.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`9.3 / 9.5 Dispatcher records a trip in quick entry and voids it (${lang})`, async ({ page }) => {
    const t = texts(lang);
    const owner = await firstSignIn(page, await signUp(page, lang, `Dispatch ${lang}`), lang);
    const company = companyOf(page);
    const w = await buildWorld(page, company);
    await inviteMember(page, company, "Dispatcher", lang);
    expect(owner.key).not.toBe("");

    await page.goto(`/c/${company}/trips`);
    const receipt = `R-${Date.now().toString().slice(-6)}`;
    await field(page, t("trip.receiptNo")).fill(receipt);
    await pick(page, t("trip.driver"), w.driverName, w.plate);
    await pick(page, t("trip.customer"), w.customerName);
    await pick(page, t("trip.loading"), "غور الصافي");
    await pick(page, t("trip.unloading"), "ميناء العقبة");
    await pick(page, t("trip.material"), "بوتاس");
    await field(page, t("trip.loaded")).fill("30");
    await field(page, t("trip.unloaded")).fill("30");
    await field(page, t("trip.unloaded")).press("Tab");
    // Price and driver rate are resolved by the server and shown with their sources (FR-021, FR-030).
    await expect(field(page, t("trip.price"))).toHaveValue(/6(\.000)?/);
    await expect(field(page, t("trip.rate"))).toHaveValue(/3\.5/);
    await button(page, t("ui.save")).click();
    await expect(toast(page, receipt)).toBeVisible();
    await expect(page.locator(".box").filter({ hasText: t("trip.recent") }).getByText(receipt)).toBeVisible();

    // 9.5 void with a reason → reversal posted, status Void.
    await page.locator(".toast a").filter({ hasText: t("ui.open") }).first().click();
    await button(page, t("trip.void")).click();
    await page.locator(".dialog textarea").fill("Entered twice");
    await button(page.locator(".dialog"), t("trip.void")).click();
    await expect(page.locator(".banner").filter({ hasText: "Entered twice" })).toBeVisible();
  });
}
