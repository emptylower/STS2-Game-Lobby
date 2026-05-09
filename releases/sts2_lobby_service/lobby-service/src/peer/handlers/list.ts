import type { Express, Request, Response } from "express";
import type { PeerStore } from "../store.js";
import type { PeersListResponse } from "../types.js";

export function mountList(app: Express, deps: { store: PeerStore }): void {
  app.get("/peers", (_req: Request, res: Response) => {
    const peers = deps.store.list().filter((p) => p.status === "active");
    const body: PeersListResponse = {
      version: 1,
      generatedAt: new Date().toISOString(),
      peers: peers.map((p) => {
        const item: PeersListResponse["peers"][number] = {
          address: p.address,
          publicKey: p.publicKey,
          lastSeen: p.lastSeen,
          status: p.status,
        };
        if (p.displayName !== undefined) {
          item.displayName = p.displayName;
        }
        return item;
      }),
    };
    res.status(200).json(body);
  });
}
