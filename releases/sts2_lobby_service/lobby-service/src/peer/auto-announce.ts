import type { PeerStore } from "./store.js";

const ANNOUNCE_TIMEOUT_MS = 5_000;

export interface AnnouncePayload {
  address: string;
  publicKey: string;
  displayName?: string;
}

export interface AutoAnnounceDeps {
  store: PeerStore;
  selfAddress: string;
  selfPublicKey: string;
  selfDisplayName?: string;
  postAnnounce?: (address: string, body: AnnouncePayload) => Promise<void>;
}

export async function announceToBootstrappedPeers(deps: AutoAnnounceDeps): Promise<void> {
  const post = deps.postAnnounce ?? defaultPostAnnounce;
  const targets = deps.store.list()
    .map((p) => p.address)
    .filter((addr) => addr !== deps.selfAddress);
  if (targets.length === 0) return;
  const body: AnnouncePayload = {
    address: deps.selfAddress,
    publicKey: deps.selfPublicKey,
    ...(deps.selfDisplayName ? { displayName: deps.selfDisplayName } : {}),
  };
  await Promise.allSettled(targets.map((addr) => post(addr, body)));
}

async function defaultPostAnnounce(address: string, body: AnnouncePayload): Promise<void> {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), ANNOUNCE_TIMEOUT_MS);
  try {
    await fetch(`${address.replace(/\/+$/, "")}/peers/announce`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(body),
      signal: ctrl.signal,
    });
  } finally {
    clearTimeout(timer);
  }
}
