import { expect, test } from "@playwright/test";
import { api, c } from "../support/api";
import { companyOf, firstSignIn, signUp, type Lang } from "../support/auth";
import { texts } from "../support/text";
import { button, field, toast } from "../support/ui";
import { buildWorld, inviteMember, today } from "../support/world";

/**
 * Owner, Administrator and Viewer (PRD 3.5), in Arabic RTL and English:
 *  9.2 month-start rates — a new loading site gets its own- and external-fleet rates on the monthly rates screen;
 *  9.12 month-end close — the previous month closed after the pre-close check, with a reason;
 *  9.14 field configuration (Administrator) — a custom trip field added, then shown on the trip form;
 *  Viewer — read-only: trips open as the list (no quick entry), no "New trip" in the menu, and the API refuses a write.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`9.2 / 9.12 / 9.14 Owner rates and close, Administrator fields, Viewer read-only (${lang})`, async ({ page }) => {
    const t = texts(lang);
    await firstSignIn(page, await signUp(page, lang, `Owner ${lang}`), lang);
    const company = companyOf(page);
    await buildWorld(page, company);

    // 9.2 Rates for a new loading site this month.
    const site = `محجر الحسا ${Date.now().toString(36).slice(-4)}`;
    await api(page, "POST", c(company, "loading-sites"), { name: site });
    await page.goto(`/c/${company}/masters/monthly-rates`);
    await page.getByRole("textbox", { name: t("rates.ownFor", site) }).fill("3.750");
    await page.getByRole("textbox", { name: t("rates.externalFor", site) }).fill("4.500");
    await button(page, t("ui.save")).click();
    await expect(toast(page, today().slice(0, 7))).toBeVisible();

    // 9.12 Close the previous month.
    const [y, m] = today().split("-").map(Number);
    const previous = m === 1 ? `${y - 1}-12` : `${y}-${String(m - 1).padStart(2, "0")}`;
    await page.goto(`/c/${company}/accounting/periods`);
    if (m === 1) await page.getByRole("button", { name: t("per.prevYear") }).click();
    await page.getByRole("row").filter({ hasText: previous }).getByRole("button", { name: t("per.close"), exact: true }).click();
    const dialog = page.locator(".dialog");
    await expect(dialog.locator(".banner")).toBeVisible();   // the pre-close check result (FR-134)
    if (await dialog.getByRole("checkbox", { name: t("per.confirm") }).count()) await dialog.getByRole("checkbox", { name: t("per.confirm") }).check();
    await dialog.locator("textarea").fill(lang === "ar" ? "إقفال نهاية الشهر" : "Month-end close");
    await dialog.getByRole("button", { name: t("per.close"), exact: true }).last().click();   // the footer action, not the ✕ (also "Close")
    await expect(toast(page, previous)).toBeVisible();

    // 9.14 Administrator adds a custom trip field; the trip form shows it.
    await inviteMember(page, company, "Administrator", lang);
    await page.goto(`/c/${company}/settings/fields`);
    await button(page, t("fld.newCustom")).click();
    const label = lang === "ar" ? "رقم الحاوية" : "Container no.";
    const modal = page.locator(".dialog");
    await field(modal, t("acc.nameAr")).fill(label);
    await field(modal, t("fld.key")).fill(`container${lang}`);
    await button(modal, t("ui.save")).click();
    await expect(page.getByRole("cell", { name: label, exact: true })).toBeVisible();   // exact: the order arrows (DT-186) name the field too
    await page.goto(`/c/${company}/trips/new`);
    await expect(page.getByRole("textbox", { name: label })).toBeVisible();

    // Viewer: read-only.
    await inviteMember(page, company, "Viewer", lang);
    await page.goto(`/c/${company}/trips`);
    await expect(page).toHaveURL(/trips\/list/);
    await expect(page.getByRole("link", { name: t("nav.newTrip"), exact: true })).toHaveCount(0);
    const refused = await page.request.post(c(company, "customers"), { data: { name: "x" } });
    expect([400, 403]).toContain(refused.status());
  });
}
