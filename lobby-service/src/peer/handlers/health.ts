// lobby-service/src/peer/handlers/health.ts
import type { Express, Request, Response } from "express";
import type { NodeIdentity } from "../identity.js";
import { signChallenge } from "../identity.js";
import type { HealthResponse } from "../types.js";

interface Deps {
  identity: NodeIdentity;
  address: string;
  // Resolved at request time so the operator can change the display name via
  // the admin panel without restarting the service.
  getDisplayName?: () => string;
  // Resolved at request time so the operator's opt-in toggle propagates
  // without a restart. Defaults to true if not provided.
  getPublicListing?: () => boolean;
}

export function mountHealth(app: Express, deps: Deps): void {
  app.get("/peers/health", (req: Request, res: Response) => {
    const challenge = typeof req.query.challenge === "string" ? req.query.challenge : "";
    if (!challenge || challenge.length > 256) {
      res.status(400).json({ error: "challenge_required" });
      return;
    }
    const body: HealthResponse = {
      address: deps.address,
      publicKey: deps.identity.publicKey,
      challenge,
      signature: signChallenge(deps.identity, challenge),
      serverTime: new Date().toISOString(),
      publicListing: deps.getPublicListing?.() ?? true,
    };
    const displayName = deps.getDisplayName?.().trim();
    if (displayName) {
      body.displayName = displayName;
    }
    res.status(200).json(body);
  });
}
