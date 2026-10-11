import { expect, type Page } from "@playwright/test";
import { api, c } from "./api";
import { firstSignIn, submitForm, uniqueEmail, type Account, type Lang } from "./auth";
import { PASSWORD } from "./env";
import { linkFromMail } from "./mail";

/** Today in the company's time zone (the default market is Jordan). */
export function today(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Amman" }).format(new Date());
}

export interface World {
  company: string;
  customer: string;
  customerName: string;
  driver: string;
  driverName: string;
  plate: string;
  material: string;
  loading: string;
  unloading: string;
  materialName: string;
  loadingName: string;
  unloadingName: string;
}

/**
 * Master data for the flows (prepared through the API by the signed-in Owner): the approved transport chart with its account mapping,
 * a customer with 30-day terms, a material, a loading and an unloading site, an own-fleet driver with a truck, a customer tariff of
 * 6.000 per tonne and this month's driver rates — what PRD 9.1 steps 6–9 leave behind.
 */
export async function buildWorld(page: Page, company: string): Promise<World> {
  await api(page, "POST", c(company, "accounting/accounts/template"), {});
  const n = Date.now().toString(36).slice(-4);
  const customerName = `البوتاس العربية ${n}`;
  const customer = await api<{ id: string }>(page, "POST", c(company, "customers"),
    { name: customerName, taxNumber: "200014587", phone: "0781234567", paymentTermsDays: 30 });
  const materialName = `بوتاس ${n}`, loadingName = `غور الصافي ${n}`, unloadingName = `ميناء العقبة ${n}`;
  const material = await api<{ id: string }>(page, "POST", c(company, "materials"), { name: materialName });
  const loading = await api<{ id: string }>(page, "POST", c(company, "loading-sites"), { name: loadingName });
  const unloading = await api<{ id: string }>(page, "POST", c(company, "unloading-sites"), { name: unloadingName });
  const types = await api<{ id: string; isBuiltIn: boolean }[]>(page, "GET", c(company, "ownership-types"));
  const year = Number(today().slice(0, 4)) + 1;
  const plate = `10-${Math.floor(10000 + Math.random() * 89999)}`;
  const driverName = `أحمد صالح ${n}`;
  const driver = await api<{ id: string }>(page, "POST", c(company, "drivers"), {
    fullName: driverName, nationalId: String(9_000_000_000 + Math.floor(Math.random() * 999_999_999)), phone1: "0791234567", licenceNo: "L-1", licenceType: "C",
    licenceExpiry: `${year}-01-01`, paymentMethod: "Cash", plateNumber: plate, truckType: "Volvo", capacityTons: 32,
    ownershipTypeId: types.find((t) => t.isBuiltIn)!.id, registrationExpiry: `${year}-01-01`, insuranceExpiry: `${year}-01-01`, inspectionExpiry: `${year}-01-01`,
  });
  await api(page, "POST", c(company, "tariffs"), { partyType: "Customer", partyId: customer.id, price: 6, validFrom: `${today().slice(0, 7)}-01` });
  await api(page, "PUT", c(company, `monthly-rates/${today().slice(0, 7)}`), { sites: [{ loadingSiteId: loading.id, ownFleetRate: 3.5, externalFleetRate: 4.25 }] });
  return { company, customer: customer.id, customerName, driver: driver.id, driverName, plate, material: material.id, loading: loading.id, unloading: unloading.id,
    materialName, loadingName, unloadingName };
}

/** A trip saved through the API (data for invoicing and settlement flows). */
export async function addTrip(page: Page, w: World, receiptNo: string, weight = 30): Promise<string> {
  const r = await api<{ trip: { id: string } }>(page, "POST", c(w.company, "trips"), {
    receiptNo, tripDate: today(), driverId: w.driver, customerId: w.customer, loadingSiteId: w.loading, unloadingSiteId: w.unloading, materialId: w.material,
    loadedWeight: weight, unloadedWeight: weight,
  });
  return r.trip.id;
}

/**
 * A staff member with a preset role (PRD 3.5): invited by the Owner (FR-172), accepts the e-mailed invitation as a new user, signs in and
 * sets up two-step verification. Leaves the browser signed in as that user.
 */
export type PresetRole = "Administrator" | "Accountant" | "Dispatcher" | "Viewer";

/** The Owner (signed in on this page) invites an e-mail address with a preset role (FR-172); returns the address. */
export async function invite(page: Page, company: string, role: PresetRole): Promise<string> {
  const roles = await api<{ id: string; name: string; isPreset: boolean }[]>(page, "GET", c(company, "roles"));
  const email = uniqueEmail(role.toLowerCase());
  await api(page, "POST", c(company, "invitations"), { email, role: roles.find((r) => r.isPreset && r.name === role)!.id });
  return email;
}

/** Accepts an e-mailed invitation as a new user, then signs in with the mandatory two-step set-up. */
export async function acceptInvitation(page: Page, email: string, role: PresetRole, lang: Lang): Promise<Account> {
  await page.context().clearCookies();
  await page.goto(await linkFromMail(email, "Invitation"));
  await page.locator('[name="Input.FullName"]').fill(`${role} E2E`);
  await page.locator('[name="Input.Password"]').fill(PASSWORD);
  await submitForm(page, "accept-new");
  await expect(page.locator("a.btn.pri.w100")).toBeVisible();
  return firstSignIn(page, { email, password: PASSWORD, key: "" }, lang);
}

/**
 * A staff member with a preset role (PRD 3.5): invited by the Owner, accepts as a new user, signs in and sets up two-step verification.
 * Leaves this browser signed in as that user.
 */
export async function inviteMember(page: Page, company: string, role: PresetRole, lang: Lang): Promise<Account> {
  return acceptInvitation(page, await invite(page, company, role), role, lang);
}
