import { execFileSync, spawn } from "node:child_process";
import fs from "node:fs";
import https from "node:https";
import path from "node:path";
import { BASE_URL, CONNECTION, DB_NAME, MAIL_DIR, ROOT, STATE_FILE, WEB_PROJECT, hostEnv } from "./env";

/**
 * Fresh E2E environment: recreates DirectServiceMaster_E2E with the approved collation (Q-07) and read-committed snapshot, applies the
 * migrations (schema + reference data), empties the e-mail drop folder, creates the platform staff account through the operations
 * command (DT-131) and starts the host on its own port. The host is built beforehand (`dotnet build`); the tests never build it.
 */
export default async function globalSetup(): Promise<void> {
  const sql = `IF DB_ID(N'${DB_NAME}') IS NOT NULL BEGIN ALTER DATABASE [${DB_NAME}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${DB_NAME}]; END;
CREATE DATABASE [${DB_NAME}] COLLATE Arabic_100_CI_AI_SC_UTF8;
ALTER DATABASE [${DB_NAME}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;`;
  execFileSync("sqlcmd", ["-S", "localhost", "-E", "-I", "-b", "-Q", sql], { stdio: "inherit" });
  const infrastructure = path.join(ROOT, "src", "Hosts", "DirectService.Database");   // migrations + design-time factory (Stage 2 session 3)
  execFileSync("dotnet", ["ef", "database", "update", "--no-build", "--project", infrastructure, "--startup-project", infrastructure, "--connection", CONNECTION],
    { stdio: "inherit", cwd: ROOT });

  fs.rmSync(path.join(path.dirname(STATE_FILE), "staff.json"), { force: true });   // the staff account is new in the new database
  fs.rmSync(MAIL_DIR, { recursive: true, force: true });
  fs.mkdirSync(MAIL_DIR, { recursive: true });

  execFileSync("dotnet", ["run", "--no-build", "--no-launch-profile", "--project", WEB_PROJECT, "--", "create-staff", "staff@e2e.test", "E2E", "Support"],
    { stdio: "inherit", cwd: ROOT, env: hostEnv() });

  const host = spawn("dotnet", ["run", "--no-build", "--no-launch-profile", "--project", WEB_PROJECT], { cwd: ROOT, env: hostEnv(), stdio: "ignore", detached: false });
  fs.mkdirSync(path.dirname(STATE_FILE), { recursive: true });
  fs.writeFileSync(STATE_FILE, JSON.stringify({ pid: host.pid }));
  await waitForHealth(`${BASE_URL}/health/live`, 120_000);
}

function waitForHealth(url: string, timeoutMs: number): Promise<void> {
  const until = Date.now() + timeoutMs;
  return new Promise((resolve, reject) => {
    const attempt = () => {
      https.get(url, { rejectUnauthorized: false }, (res) => (res.statusCode === 200 ? resolve() : retry())).on("error", retry);
    };
    const retry = () => (Date.now() > until ? reject(new Error(`The E2E host did not start at ${url}.`)) : setTimeout(attempt, 1000));
    attempt();
  });
}
