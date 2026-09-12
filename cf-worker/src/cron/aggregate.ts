import type { Env } from "../index.js";
import {
  KV_KEY_ACTIVE,
  KV_KEY_SEEDS,
  type ActiveServersDocument,
  type PeerEntry,
  type SeedsDocument,
} from "../types.js";

type FetchLike = (input: RequestInfo, init?: RequestInit) => Promise<Response>;

const SAMPLER_MAX = 8;
const PEER_FETCH_TIMEOUT_MS = 5_000;
// How long to keep a peer in the public list after we last successfully
// observed it. Without this, transient unreachability from CF edge (e.g.
// CN-side ISPs filtering port 8787 outbound) silently evicts good lobbies
// that are still serving players just fine. 24h is generous enough to ride
// out daytime/nighttime routing flaps without accumulating dead entries.
const OFFLINE_RETENTION_MS = 24 * 3600_000;

interface PeersResponse {
  peers: PeerEntry[];
}

export async function aggregateActivePeers(env: Env, fetchImpl: FetchLike = fetch): Promise<void> {
  const seeds = await loadSeeds(env);
  const previous = await loadActive(env);

  const samplerAddresses = selectSamplerAddresses(seeds, previous);

  const fetched = await Promise.allSettled(
    samplerAddresses.map((addr) => fetchPeers(addr, fetchImpl)),
  );

  const merged = new Map<string, PeerEntry>();

  for (const r of fetched) {
    if (r.status !== "fulfilled") continue;
    for (const peer of r.value) {
      merged.set(peer.address, peer);
    }
  }

  // Preserve previous-active entries that didn't refresh this tick, up to
  // OFFLINE_RETENTION_MS. Reachability-from-CF is not the same as
  // reachability-from-players; dropping a peer just because CF edge
  // momentarily can't reach it is a false negative that hides healthy
  // servers from clients.
  const cutoff = Date.now() - OFFLINE_RETENTION_MS;
  for (const p of previous) {
    if (merged.has(p.address)) continue;
    const lastSeenMs = Date.parse(p.lastSeen);
    if (Number.isFinite(lastSeenMs) && lastSeenMs < cutoff) continue;
    merged.set(p.address, p);
  }

  // Public-listing opt-in: ask each candidate directly whether it wants to
  // be in the public list. Older nodes (pre-v0.4) don't expose this and
  // are treated as public by default for backwards compatibility. Failures
  // also keep the peer — we don't drop reachable-to-players nodes just
  // because CF edge can't reach them this tick.
  const filterResults = await Promise.allSettled(
    [...merged.keys()].map(async (addr) => {
      if (isIpLiteralAddress(addr)) return { addr, wantsListing: null };
      const wantsListing = await fetchWantsPublicListing(addr, fetchImpl);
      return { addr, wantsListing };
    }),
  );
  for (const r of filterResults) {
    if (r.status !== "fulfilled") continue;
    if (r.value.wantsListing === false) {
      merged.delete(r.value.addr);
    }
  }

  if (merged.size === 0 && previous.length > 0) {
    return;
  }

  const document: ActiveServersDocument = {
    version: 1,
    updated_at: new Date().toISOString(),
    servers: [...merged.values()],
  };
  await env.DISCOVERY_KV.put(KV_KEY_ACTIVE, JSON.stringify(document));
}

export function isIpLiteralAddress(address: string): boolean {
  let hostname: string;
  try {
    hostname = new URL(address).hostname;
  } catch {
    return true;
  }
  if (hostname.startsWith("[") && hostname.endsWith("]")) return true;
  if (!/^\d{1,3}(\.\d{1,3}){3}$/.test(hostname)) return false;
  return hostname.split(".").every((part) => Number(part) <= 255);
}

function selectSamplerAddresses(
  seeds: Array<{ address: string }>,
  previous: PeerEntry[],
): string[] {
  const selected: string[] = [];
  const seen = new Set<string>();
  for (const s of seeds) {
    if (isIpLiteralAddress(s.address) || seen.has(s.address)) continue;
    seen.add(s.address);
    selected.push(s.address);
  }
  if (selected.length >= SAMPLER_MAX) return selected.slice(0, SAMPLER_MAX);

  const candidates: string[] = [];
  for (const p of previous) {
    if (isIpLiteralAddress(p.address) || seen.has(p.address)) continue;
    seen.add(p.address);
    candidates.push(p.address);
  }
  for (let i = candidates.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [candidates[i], candidates[j]] = [candidates[j], candidates[i]];
  }
  selected.push(...candidates.slice(0, SAMPLER_MAX - selected.length));
  return selected;
}

// Returns true if the peer explicitly wants to be public, false if it
// explicitly opted out, or null when we couldn't determine (treated as
// "leave in" by the caller).
async function fetchWantsPublicListing(
  address: string,
  fetchImpl: FetchLike,
): Promise<boolean | null> {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), PEER_FETCH_TIMEOUT_MS);
  try {
    const res = await fetchImpl(`${address.replace(/\/+$/, "")}/peers/metrics`, { signal: ctrl.signal });
    if (!res.ok) return null;
    const body = (await res.json()) as { publicListing?: unknown };
    if (typeof body.publicListing === "boolean") return body.publicListing;
    return null;
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

async function loadSeeds(env: Env): Promise<Array<{ address: string }>> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_SEEDS);
  if (!raw) return [];
  return (JSON.parse(raw) as SeedsDocument).seeds;
}

async function loadActive(env: Env): Promise<PeerEntry[]> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_ACTIVE);
  if (!raw) return [];
  return (JSON.parse(raw) as ActiveServersDocument).servers;
}

async function fetchPeers(address: string, fetchImpl: FetchLike): Promise<PeerEntry[]> {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), PEER_FETCH_TIMEOUT_MS);
  try {
    const res = await fetchImpl(`${address.replace(/\/+$/, "")}/peers`, { signal: ctrl.signal });
    if (!res.ok) throw new Error(`http_${res.status}`);
    const body = (await res.json()) as PeersResponse;
    return body.peers ?? [];
  } finally {
    clearTimeout(timer);
  }
}
