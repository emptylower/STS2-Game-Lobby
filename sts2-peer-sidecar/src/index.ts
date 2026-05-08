import express from "express";
import { join } from "node:path";
import {
  loadOrCreateIdentity, PeerStore,
  mountHealth, mountList, mountAnnounce, mountHeartbeat,
  GossipScheduler, loadSeedsFromCf, bootstrapPeers,
} from "./use-peer-modules.js";

interface Config {
  listenPort: number;
  lobbyPublicBaseUrl: string;
  cfDiscoveryBaseUrl: string;
  stateDir: string;
}

function loadConfig(): Config {
  const lobbyUrl = process.env.LOBBY_PUBLIC_BASE_URL;
  if (!lobbyUrl) throw new Error("LOBBY_PUBLIC_BASE_URL is required");
  return {
    listenPort: Number.parseInt(process.env.PEER_LISTEN_PORT ?? "18800", 10),
    lobbyPublicBaseUrl: lobbyUrl,
    cfDiscoveryBaseUrl: process.env.PEER_CF_DISCOVERY_BASE_URL ?? "",
    stateDir: process.env.PEER_STATE_DIR ?? "./data",
  };
}

async function main(): Promise<void> {
  const cfg = loadConfig();
  const identity = await loadOrCreateIdentity(cfg.stateDir);
  const store = new PeerStore(join(cfg.stateDir, "peers.json"));
  await store.load();

  const app = express();
  app.use(express.json());
  mountHealth(app, { identity, address: cfg.lobbyPublicBaseUrl });
  mountList(app, { store });
  mountAnnounce(app, { store });
  mountHeartbeat(app, { store });

  if (cfg.cfDiscoveryBaseUrl) {
    const seeds = await loadSeedsFromCf(cfg.cfDiscoveryBaseUrl);
    await bootstrapPeers({ store, selfAddress: cfg.lobbyPublicBaseUrl, seeds });
  }

  const scheduler = new GossipScheduler({
    store, selfAddress: cfg.lobbyPublicBaseUrl, selfPublicKey: identity.publicKey,
    seedAddresses: [],
    postHeartbeat: async (addr, body) => {
      await fetch(`${addr.replace(/\/+$/, "")}/peers/heartbeat`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
    },
  });
  scheduler.start();

  app.listen(cfg.listenPort, () => {
    console.log(`[sidecar] listening on ${cfg.listenPort}; representing ${cfg.lobbyPublicBaseUrl}`);
  });
}

main().catch((err) => { console.error(err); process.exit(1); });
