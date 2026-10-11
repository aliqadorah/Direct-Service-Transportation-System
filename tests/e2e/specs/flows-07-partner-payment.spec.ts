import { expect, test } from "@playwright/test";
import { api, c } from "../support/api";
import { companyOf, firstSignIn, signUp, type Lang } from "../support/auth";
import { texts } from "../support/text";
import { button, choose, field, toast } from "../support/ui";
import { buildWorld, inviteMember, today } from "../support/world";

/**
 * PRD 9.11 partner payment (Accountant), in Arabic RTL and English: a partner's truck carries a trip at the partner tariff (partner cost
 * posted, driver wage 0); the partner's balance due is shown; a payment above it is refused naming the balance; a payment within it is
 * saved with its PPY- number.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`9.11 Accountant pays a partner within the balance due (${lang})`, async ({ page }) => {
    const t = texts(lang);
    await firstSignIn(page, await signUp(page, lang, `Partner ${lang}`), lang);
    const company = companyOf(page);
    const w = await buildWorld(page, company);
    const n = Date.now().toString(36).slice(-4);
    const partner = await api<{ id: string }>(page, "POST", c(company, "partners"), { name: `النور للنقل ${n}` });
    const external = await api<{ id: string }>(page, "POST", c(company, "ownership-types"), { name: `شريك ${n}`, fleetClass: "External" });
    const year = Number(today().slice(0, 4)) + 1;
    const driver = await api<{ id: string }>(page, "POST", c(company, "drivers"), {
      fullName: `سائق الشريك ${n}`, nationalId: String(9_000_000_000 + Math.floor(Math.random() * 999_999_999)), phone1: "0791234568", licenceNo: "L-2", licenceType: "C", paymentMethod: "Cash",
      plateNumber: `20-${Math.floor(10000 + Math.random() * 89999)}`, truckType: "MAN", capacityTons: 30, ownershipTypeId: external.id, partnerId: partner.id,
      licenceExpiry: `${year}-01-01`, registrationExpiry: `${year}-01-01`, insuranceExpiry: `${year}-01-01`, inspectionExpiry: `${year}-01-01`,
    });
    await api(page, "POST", c(company, "tariffs"), { partyType: "Partner", partyId: partner.id, price: 5, validFrom: `${today().slice(0, 7)}-01` });
    await api(page, "POST", c(company, "trips"), {
      receiptNo: `P-1-${lang}`, tripDate: today(), driverId: driver.id, partnerId: partner.id, customerId: w.customer, loadingSiteId: w.loading, unloadingSiteId: w.unloading,
      materialId: w.material, loadedWeight: 30, unloadedWeight: 30,
    });   // partner cost 30 × 5.000 = 150.000

    await inviteMember(page, company, "Accountant", lang);
    await page.goto(`/c/${company}/partner-accounts/${partner.id}/pay`);
    await expect(page.getByText("150.000").first()).toBeVisible();   // the balance due
    await field(page, t("inv.amount")).fill("200");
    await choose(page, t("cash.cashBox"));
    await button(page, t("ui.save")).click();
    await expect(page.getByText("150.000").first()).toBeVisible();
    await expect(page.locator(".toast.err, .banner, .errsum").filter({ hasText: "150.000" }).first()).toBeVisible();   // refused, balance named
    await field(page, t("inv.amount")).fill("100");
    await button(page, t("ui.save")).click();
    await expect(toast(page, "PPY-")).toBeVisible();
  });
}
