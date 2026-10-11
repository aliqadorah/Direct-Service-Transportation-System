import { execFileSync } from "node:child_process";
import fs from "node:fs";
import { STATE_FILE } from "./env";

/** Stops the E2E host (the whole process tree started by `dotnet run`). The database is kept for inspection and recreated next run. */
export default async function globalTeardown(): Promise<void> {
  if (!fs.existsSync(STATE_FILE)) return;
  const { pid } = JSON.parse(fs.readFileSync(STATE_FILE, "utf8")) as { pid?: number };
  if (pid) {
    try {
      execFileSync("taskkill", ["/PID", String(pid), "/T", "/F"], { stdio: "ignore" });
    } catch {
      // already stopped
    }
  }
  fs.rmSync(STATE_FILE, { force: true });
}
