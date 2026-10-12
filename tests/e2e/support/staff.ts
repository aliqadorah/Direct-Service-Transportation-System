import fs from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { firstSignIn, signIn, submitForm, type Account, type Lang } from "./auth";
import { PASSWORD, STATE_FILE } from "./env";
import { linkFromMail } from "./mail";

const STAFF_FILE = path.join(path.dirname(STATE_FILE), "staff.json");

/** The platform staff account created by the operations command in global setup (DT-131): password set from the e-mailed link once. */
export async function staffSignIn(page: Page, lang: Lang): Promise<Account> {
  if (fs.existsSync(STAFF_FILE)) {
    const account = JSON.parse(fs.readFileSync(STAFF_FILE, "utf8")) as Account;
    await signIn(page, account, lang);
    return account;
  }
  await page.goto(await linkFromMail("staff@e2e.test", "PasswordReset"));
  await page.locator('[name="Input.NewPassword"]').fill(PASSWORD);
  await page.locator('[name="Input.ConfirmPassword"]').fill(PASSWORD);
  await submitForm(page, "reset");
  const account = await firstSignIn(page, { email: "staff@e2e.test", password: PASSWORD, key: "" }, lang);
  fs.writeFileSync(STAFF_FILE, JSON.stringify(account));
  return account;
}
