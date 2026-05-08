export interface PeerEntry {
  address: string;
  publicKey?: string;
  displayName?: string;
  lastSeen: string;
  note?: string;
}

export interface SeedsDocument {
  version: 1;
  updated_at: string;
  seeds: Array<{ address: string; note?: string }>;
}

export interface ActiveServersDocument {
  version: 1;
  updated_at: string;
  servers: PeerEntry[];
}

export interface AnnouncementsDocument {
  version: 1;
  updated_at: string;
  items: Array<{
    id: string;
    title: string;
    body: string;
    publishedAt: string;
  }>;
}

export const KV_KEY_SEEDS = "peers:seeds";
export const KV_KEY_ACTIVE = "peers:active";
export const KV_KEY_ANNOUNCEMENTS = "announcements";
