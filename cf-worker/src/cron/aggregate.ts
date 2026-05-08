import type { Env } from "../index.js";
import {
  KV_KEY_ACTIVE,
  KV_KEY_SEEDS,
  type ActiveServersDocument,
  type PeerEntry,
  type SeedsDocument,
} from "../types.js";

type FetchLike = (input: RequestInfo, init?: RequestInit) => Promise<Response>;

const SAMPLER_PER_SOURCE = 5;
const PEER_FETCH_TIMEOUT_MS = 5_000;

interface PeersResponse {
  peers: PeerEntry[];
}

export async function aggregateActivePeers(env: Env, fetchImpl: FetchLike = fetch): Promise<void> {
  const seeds = await loadSeeds(env);
  const previous = await loadActive(env);

  const samplerSet = new Set<string>();
  for (const s of seeds.slice(0, SAMPLER_PER_SOURCE)) samplerSet.add(s.address);
  for (const p of previous.slice(0, SAMPLER_PER_SOURCE)) samplerSet.add(p.address);

  const fetched = await Promise.allSettled(
    [...samplerSet].map((addr) => fetchPeers(addr, fetchImpl)),
  );

  const merged = new Map<string, PeerEntry>();

  for (const r of fetched) {
    if (r.status !== "fulfilled") continue;
    for (const peer of r.value) {
      merged.set(peer.address, peer);
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
