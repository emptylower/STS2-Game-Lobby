import { randomBytes } from "node:crypto";
import { verifySignature } from "./identity.js";
import type { HealthResponse } from "./types.js";

const PROBE_TIMEOUT_MS = 5_000;

export type ProbeResult = { ok: true; publicKey: string } | { ok: false; reason: string };

export async function probeAndVerify(address: string, expectedKey?: string): Promise<ProbeResult> {
  const challenge = randomBytes(16).toString("base64url");
  const ctrl = new AbortController();
  const t = setTimeout(() => ctrl.abort(), PROBE_TIMEOUT_MS);
  try {
    const url = `${address.replace(/\/+$/, "")}/peers/health?challenge=${encodeURIComponent(challenge)}`;
    const res = await fetch(url, { signal: ctrl.signal });
    if (!res.ok) return { ok: false, reason: `http_${res.status}` };
    const body = (await res.json()) as HealthResponse;
    if (body.challenge !== challenge) return { ok: false, reason: "challenge_mismatch" };
    if (!verifySignature(body.publicKey, challenge, body.signature)) return { ok: false, reason: "signature_invalid" };
    if (expectedKey && expectedKey !== body.publicKey) return { ok: false, reason: "publickey_mismatch" };
    return { ok: true, publicKey: body.publicKey };
  } catch (err) {
    return { ok: false, reason: err instanceof Error ? err.message : "unknown" };
  } finally { clearTimeout(t); }
}
