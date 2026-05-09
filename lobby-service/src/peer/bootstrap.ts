import type { PeerStore } from "./store.js";
import type { ProbeResult } from "./prober.js";
import { probeAndVerify as defaultProbe } from "./prober.js";
import type { SeedAddress } from "./seeds-loader.js";

export interface BootstrapDeps {
  store: PeerStore;
  selfAddress: string;
  seeds: SeedAddress[];
  probeAndVerify?: (address: string, expectedKey?: string) => Promise<ProbeResult>;
}

export async function bootstrapPeers(deps: BootstrapDeps): Promise<void> {
  const probe = deps.probeAndVerify ?? defaultProbe;
  for (const seed of deps.seeds) {
    if (seed.address === deps.selfAddress) continue;
    if (deps.store.get(seed.address)) continue;
    const r = await probe(seed.address);
    if (!r.ok) continue;
    const now = new Date().toISOString();
    await deps.store.upsert({
      address: seed.address, publicKey: r.publicKey,
      ...(r.displayName ? { displayName: r.displayName } : {}),
      firstSeen: now, lastSeen: now, consecutiveProbeFailures: 0,
      status: "active", source: "seed",
    });
  }
}
