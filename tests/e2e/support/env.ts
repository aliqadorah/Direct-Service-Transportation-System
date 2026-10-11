import path from "node:path";

/** Repository root (tests/e2e/support → ../../..). */
export const ROOT = path.resolve(__dirname, "..", "..", "..");
export const PORT = 52301;
export const BASE_URL = `https://localhost:${PORT}`;
export const DB_NAME = "DirectServiceMaster_E2E";
export const CONNECTION = `Server=localhost;Database=${DB_NAME};Integrated Security=true;TrustServerCertificate=true;Encrypt=true`;
export const MAIL_DIR = path.join(ROOT, ".local", "e2e", "mail");
export const STATE_FILE = path.join(ROOT, ".local", "e2e", "state.json");
export const WEB_PROJECT = path.join(ROOT, "src", "DirectService.Transportation.Web");

/** Test password for every E2E account (local test database only). */
export const PASSWORD = "E2e-Passw0rd!";

/** Environment of the E2E host and of the operations command. */
export function hostEnv(): NodeJS.ProcessEnv {
  return {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: BASE_URL,
    ConnectionStrings__MasterDb: CONNECTION,
    App__PublicBaseUrl: BASE_URL,
    Email__DropFolder: MAIL_DIR,
    RateLimits__AuthPerMinutePerIp: "1000",
    RateLimits__ApiPerMinutePerUser: "5000",
    EInvoicing__AllowSimulator: "true",   // the national e-invoicing simulator (never in Production)
  };
}
