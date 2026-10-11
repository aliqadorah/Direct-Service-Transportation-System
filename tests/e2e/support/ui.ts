import { expect, type Locator, type Page } from "@playwright/test";

/** A labelled field (the design system labels every input; required marks are hidden from the accessible name). */
export const field = (scope: Page | Locator, label: string): Locator => scope.getByRole("textbox", { name: label, exact: true }).first();

/** Picks an option of a combobox (ComboField) by typing part of its text and choosing the matching option. */
export async function pick(scope: Page | Locator, label: string, text: string, typed = text.slice(0, 12)): Promise<void> {
  const input = scope.getByRole("combobox", { name: label, exact: true }).first();
  await input.click();
  await input.fill(typed);
  const page = "page" in scope ? scope.page() : scope;
  const option = page.getByRole("option", { name: text }).first();
  await expect(option).toBeVisible();
  await option.click();
}

/** A native select by its label: chooses the option with this text, or (without text) the first real option when nothing is chosen. */
export async function choose(scope: Page | Locator, label: string, text?: string): Promise<void> {
  const select = scope.getByRole("combobox", { name: label, exact: true }).first();
  if (text) {
    await select.selectOption({ label: text });
    return;
  }
  if ((await select.inputValue()) !== "") return;
  const values = await select.locator("option").evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value).filter((v) => v !== ""));
  await select.selectOption(values[0]);
}

/** A toast with this text (success messages, UX-19). */
export const toast = (page: Page, text: string | RegExp): Locator => page.locator(".toast").filter({ hasText: text });

/** A button by its visible text (the accessible name may end with the shortcut shown on the button, e.g. "Save Ctrl S"). */
export const button = (scope: Page | Locator, text: string): Locator =>
  scope.getByRole("button", { name: new RegExp(`^${text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}(\\s|$)`) }).first();
