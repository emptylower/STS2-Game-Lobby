import type { Express, Request, Response } from "express";
import type { PeerStore } from "../store.js";
import type { HeartbeatRequestBody } from "../types.js";

export function mountHeartbeat(app: Express, deps: { store: PeerStore }): void {
  app.post("/peers/heartbeat", async (req: Request, res: Response) => {
    const body = req.body as Partial<HeartbeatRequestBody>;
    if (!body || typeof body.address !== "string" || typeof body.publicKey !== "string") {
      res.status(400).json({ error: "address_and_publicKey_required" });
      return;
    }
    const ok = await deps.store.heartbeat(body.address, body.publicKey);
    if (!ok) { res.status(404).json({ error: "unknown_peer_or_key_mismatch" }); return; }
    res.status(200).json({ accepted: true });
  });
}
