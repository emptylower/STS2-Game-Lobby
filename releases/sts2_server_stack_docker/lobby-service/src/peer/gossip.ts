import type { PeerStore } from "./store.js";
import type { ProbeResult } from "./prober.js";
import { probeAndVerify as defaultProbe } from "./prober.js";

const PULL_INTERVAL_MS = 5 * 60_000;
const PUSH_INTERVAL_MS = 30 * 60_000;
const TTL_INTERVAL_MS = 60 * 60_000;
const PULL_FANOUT = 3;
const PUSH_FANOUT = 3;
const OFFLINE_AFTER_MS = 24 * 3600_000;

export interface GossipDeps {
  store: PeerStore;
  selfAddress: string;
  selfPublicKey?: string;
  seedAddresses: string[];
  fetchPeers?: (address: string) => Promise<Array<{ address: string; publicKey: string; lastSeen: string; displayName?: string }>>;
  probeAndVerify?: (address: string, expectedKey?: string) => Promise<ProbeResult>;
  postHeartbeat?: (address: string, body: { address: string; publicKey: string }) => Promise<void>;
}

export class GossipScheduler {
  private timers: NodeJS.Timeout[] = [];
  constructor(private readonly deps: GossipDeps) {}

  start(): void {
    this.timers.push(setInterval(() => { void this.runPullCycleOnce(); }, PULL_INTERVAL_MS));
    this.timers.push(setInterval(() => { void this.runPushCycleOnce(); }, PUSH_INTERVAL_MS));
    this.timers.push(setInterval(() => { this.runTtlCycleOnce(); }, TTL_INTERVAL_MS));
  }

  stop(): void { for (const t of this.timers) clearInterval(t); this.timers = []; }

  async runPullCycleOnce(): Promise<void> {
    const peers = this.deps.store.list().filter((p) => p.status === "active" && p.address !== this.deps.selfAddress);
    const sample = pickRandom(peers.map((p) => p.address), PULL_FANOUT);
    if (sample.length === 0) return;
    const fetcher = this.deps.fetchPeers ?? defaultFetchPeers;
    const probe = this.deps.probeAndVerify ?? defaultProbe;
    const merged = new Map<string, { address: string; publicKey: string; lastSeen: string; displayName?: string }>();
    for (const addr of sample) {
      try {
        for (const p of await fetcher(addr)) merged.set(p.address, p);
      } catch { /* ignore peer-level failure */ }
    }
    for (const p of merged.values()) {
      if (p.address === this.deps.selfAddress) continue;
      if (this.deps.store.get(p.address)) continue;
      const probed = await probe(p.address, p.publicKey);
      if (!probed.ok) continue;
      // Prefer the displayName we just verified directly from the peer's
      // /peers/health response over the value forwarded by the gossip source,
      // since the gossip-list copy can be stale.
      const displayName = probed.displayName ?? p.displayName;
      await this.deps.store.upsert({
        address: p.address, publicKey: probed.publicKey,
        ...(displayName ? { displayName } : {}),
        firstSeen: new Date().toISOString(), lastSeen: p.lastSeen,
        consecutiveProbeFailures: 0, status: "active", source: "gossip",
      });
    }
  }

  async runPushCycleOnce(): Promise<void> {
    if (!this.deps.selfPublicKey || !this.deps.postHeartbeat) return;
    const peers = this.deps.store.list().filter((p) => p.status === "active" && p.address !== this.deps.selfAddress);
    const sample = pickRandom(peers.map((p) => p.address), PUSH_FANOUT);
    for (const addr of sample) {
      try {
        await this.deps.postHeartbeat!(addr, { address: this.deps.selfAddress, publicKey: this.deps.selfPublicKey! });
      } catch { /* ignore */ }
    }
  }

  runTtlCycleOnce(now = new Date()): void {
    const offlineCutoff = now.getTime() - OFFLINE_AFTER_MS;
    for (const p of this.deps.store.list()) {
      if (p.status === "active" && new Date(p.lastSeen).getTime() < offlineCutoff) {
        p.status = "offline";
        this.deps.store.scheduleFlush();
      }
    }
    this.deps.store.runTtlCleanup(now);
  }
}

function pickRandom<T>(arr: T[], n: number): T[] {
  const copy = arr.slice();
  for (let i = copy.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    const tmp = copy[i]!;
    copy[i] = copy[j]!;
    copy[j] = tmp;
  }
  return copy.slice(0, n);
}

async function defaultFetchPeers(address: string): Promise<Array<{ address: string; publicKey: string; lastSeen: string; displayName?: string }>> {
  const ctrl = new AbortController();
  const t = setTimeout(() => ctrl.abort(), 5_000);
  try {
    const res = await fetch(`${address.replace(/\/+$/, "")}/peers`, { signal: ctrl.signal });
    if (!res.ok) return [];
    const body = (await res.json()) as { peers?: Array<{ address: string; publicKey: string; lastSeen: string; displayName?: string }> };
    return body.peers ?? [];
  } catch { return []; } finally { clearTimeout(t); }
}
