import type { Page } from "@playwright/test";

/**
 * JSON API calls in the signed-in browser session (same cookies), with the antiforgery header the API requires on writes (SEC-12).
 * Used only to prepare data a flow needs; the flow under test itself is driven through the screens.
 */
export async function api<T = unknown>(page: Page, method: "GET" | "POST" | "PUT", path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {};
  if (method !== "GET") {
    const af = await (await page.request.get("/api/v1/auth/antiforgery")).json();
    headers[af.headerName] = af.token;
  }
  const res = await page.request.fetch(path, { method, data: body, headers });
  if (!res.ok()) throw new Error(`${method} ${path} → ${res.status()} ${await res.text()}`);
  return (res.status() === 204 ? undefined : await res.json()) as T;
}

export const c = (company: string, path: string) => `/api/v1/companies/${encodeURIComponent(company)}/${path.replace(/^\//, "")}`;
