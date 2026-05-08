import type { Express, Request, Response } from "express";
import { probeAndVerify } from "../prober.js";
import type { PeerStore } from "../store.js";
import type { AnnounceRequestBody } from "../types.js";

const ANNOUNCE_RATE_LIMIT_PER_HOUR = 5;
const recentByIp: Map<string, number[]> = new Map();

export function mountAnnounce(app: Express, deps: { store: PeerStore }): void {
  app.post("/peers/announce", async (req: Request, res: Response) => {
    const body = req.body as Partial<AnnounceRequestBody>;
    if (!body || typeof body.address !== "string" || typeof body.publicKey !== "string") {
      res.status(400).json({ error: "address_and_publicKey_required" });
      return;
    }

    const ip = (req.ip ?? "0.0.0.0").toString();
    if (rateLimited(ip)) { res.status(429).json({ error: "rate_limited" }); return; }

    const probe = await probeAndVerify(body.address, body.publicKey);
    if (!probe.ok) {
      res.status(422).json({ error: "probe_failed", reason: probe.reason });
      return;
    }

    const now = new Date();
    await deps.store.upsert({
      address: body.address,
      publicKey: probe.publicKey,
      ...(body.displayName !== undefined && { displayName: body.displayName }),
      firstSeen: deps.store.get(body.address)?.firstSeen ?? now.toISOString(),
      lastSeen: now.toISOString(),
      consecutiveProbeFailures: 0,
      status: "active",
      source: "announce",
    });
    res.status(202).json({ accepted: true });
  });
}

function rateLimited(ip: string): boolean {
  const now = Date.now();
  const cutoff = now - 3600_000;
  const list = (recentByIp.get(ip) ?? []).filter((t) => t > cutoff);
  if (list.length >= ANNOUNCE_RATE_LIMIT_PER_HOUR) { recentByIp.set(ip, list); return true; }
  list.push(now); recentByIp.set(ip, list); return false;
}
