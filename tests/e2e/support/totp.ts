import crypto from "node:crypto";

/** The code (RFC 6238: SHA-1, 6 digits, 30 s) of a base32 key for one time step. */
export function totpAt(base32Key: string, step: number): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const clean = base32Key.replace(/[\s=]/g, "").toUpperCase();
  let bits = "";
  for (const c of clean) bits += alphabet.indexOf(c).toString(2).padStart(5, "0");
  const key = Buffer.from((bits.match(/.{8}/g) ?? []).map((b) => parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const h = crypto.createHmac("sha1", key).update(counter).digest();
  const o = h[h.length - 1] & 15;
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).toString().padStart(6, "0");
}

const lastStep = new Map<string, number>();

/**
 * A fresh authenticator code, as the app shows it. The server accepts a code once only (DT-212) and the current step ±1, so each call
 * uses a later step than the previous call for the same key — the previous, current or next one; when all are used it waits for the next
 * 30-second step, like a person waiting for the app's next code.
 */
export async function totp(base32Key: string): Promise<string> {
  for (;;) {
    const now = Math.floor(Date.now() / 1000 / 30);
    // The server accepts now ± 1; the previous step only while it cannot expire on the way (more than 3 s left in this step).
    const earliest = 30 - (Math.floor(Date.now() / 1000) % 30) > 3 ? now - 1 : now;
    const step = Math.max(earliest, (lastStep.get(base32Key) ?? -1) + 1);
    if (step <= now + 1) {
      lastStep.set(base32Key, step);
      return totpAt(base32Key, step);
    }
    await new Promise((r) => setTimeout(r, ((now + 1) * 30 - Date.now() / 1000) * 1000 + 250));
  }
}
