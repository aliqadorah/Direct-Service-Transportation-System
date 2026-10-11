import fs from "node:fs";
import path from "node:path";
import { expect, test } from "@playwright/test";
import { api, c } from "../support/api";
import { companyOf, firstSignIn, signUp, type Lang } from "../support/auth";
import { STATE_FILE } from "../support/env";
import { texts } from "../support/text";
import { button } from "../support/ui";
import { acceptInvitation, addTrip, buildWorld, invite, today } from "../support/world";

/**
 * In Arabic RTL and English:
 *  9.4 import trips — a CSV with a valid row and a row naming an unknown driver: matched columns, preview with the rejected row and its
 *      reason, strict mode blocks the import, tolerant mode imports the valid row;
 *  9.7 e-invoicing — settings with the national-system simulator, an approved invoice queued, submitted from the e-invoicing screen →
 *      Submitted;
 *  9.13 concurrent work — while the Owner has a trip open, the Dispatcher sees "… is editing this record" and the form read-only.
 */
for (const lang of ["ar", "en"] as Lang[]) {
  test(`9.4 / 9.7 / 9.13 Import, e-invoicing submission and record lock (${lang})`, async ({ page, browser }) => {
    const t = texts(lang);
    await firstSignIn(page, await signUp(page, lang, `Import ${lang}`), lang);
    const company = companyOf(page);
    const w = await buildWorld(page, company);

    // 9.4 Import trips.
    const csv = path.join(path.dirname(STATE_FILE), `trips-${lang}.csv`);
    fs.writeFileSync(csv, [
      "Receipt no,Date,Plate,Customer,Loading site,Unloading site,Material,Loaded weight,Unloaded weight",
      `IMP-1-${lang},${today()},${w.plate},${w.customerName},${w.loadingName},${w.unloadingName},${w.materialName},31.5,31.2`,
      `IMP-2-${lang},${today()},99-99999,${w.customerName},${w.loadingName},${w.unloadingName},${w.materialName},30,30`,
    ].join("\r\n"), "utf8");
    await page.goto(`/c/${company}/trips/import`);
    await page.locator("#trip-file").setInputFiles(csv);
    await page.getByRole("radio", { name: t("trip.strict") }).check();
    await expect(button(page, t("trip.importValid", 1))).toBeDisabled();   // strict: the unknown driver blocks the file
    await expect(page.getByRole("row").filter({ hasText: `IMP-2-${lang}` })).toBeVisible();
    await page.getByRole("radio", { name: t("trip.tolerant") }).check();
    await button(page, t("imp.preview")).click();
    await button(page, t("trip.importValid", 1)).click();
    await expect(page.locator(".dialog").filter({ hasText: t("trip.importDone") })).toBeVisible();

    // 9.7 E-invoicing with the simulator.
    const settings = await api<{ rowVersion: string }>(page, "GET", c(company, "settings/e-invoicing"));
    await api(page, "PUT", c(company, "settings/e-invoicing"), { isEnabled: true, connector: "Simulator", sellerTaxNumber: "012345678", registrationType: "Sales",
      incomeSourceSequence: "9912044", clientId: "e2e-client", secretKey: "e2e-secret", environment: "Test", rowVersion: settings.rowVersion });
    const trip = await addTrip(page, w, `EI-${lang}`);
    const draft = await api<{ invoice: { id: string; rowVersion: string } }>(page, "POST", c(company, "invoices"),
      { customerId: w.customer, invoiceType: "Trip", invoiceDate: today(), tripIds: [trip], discount: 0, extraFees: 0, approve: true });
    // Submitted automatically after approval (Queued → Submitted, 9.7 step 2): the status screen shows it once the queue has run.
    await expect.poll(async () => {
      await page.goto(`/c/${company}/e-invoicing`);
      const row = page.getByRole("row").filter({ hasText: "INV-00001" });
      await expect(row).toBeVisible();
      return (await row.textContent()) ?? "";
    }, { timeout: 60_000, intervals: [2_000] }).toContain(t("st.submitted"));
    expect(draft.invoice.id).toBeTruthy();

    // 9.13 Record lock: the Owner holds the trip, the Dispatcher sees it read-only.
    const open = await addTrip(page, w, `LK-${lang}`);   // an editable trip (the invoiced one is read-only and takes no lock)
    await page.goto(`/c/${company}/trips/${open}`);
    await expect(page.locator("main h1")).toBeVisible();
    const email = await invite(page, company, "Dispatcher");
    const locked = await (await browser.newContext()).newPage();
    await acceptInvitation(locked, email, "Dispatcher", lang);
    await locked.goto(`/c/${company}/trips/${open}`);
    const ownerName = "مالك الاختبار";
    await expect(locked.locator(".banner").filter({ hasText: ownerName })).toBeVisible();   // "… is editing this record" (BR-039)
    await expect(locked.getByRole("button", { name: t("trip.void") })).toHaveCount(0);   // read-only while locked
  });
}
