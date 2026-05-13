// lobby-service/src/peer/types.ts
export interface PeerRecord {
  address: string;
  publicKey: string;
  displayName?: string;
  firstSeen: string;
  lastSeen: string;
  consecutiveProbeFailures: number;
  status: "active" | "offline";
  source: "self" | "seed" | "announce" | "gossip";
}

export interface PeersListResponse {
  version: 1;
  generatedAt: string;
  peers: Array<{
    address: string;
    publicKey: string;
    displayName?: string;
    lastSeen: string;
    status: "active" | "offline";
  }>;
}

export interface AnnounceRequestBody {
  address: string;
  publicKey: string;
  displayName?: string;
}

export interface HeartbeatRequestBody {
  address: string;
  publicKey: string;
}

export interface HealthResponse {
  address: string;
  publicKey: string;
  challenge: string;
  signature: string;
  serverTime: string;
  // Human-readable name set by the operator (admin panel or PEER_DISPLAY_NAME).
  // Optional — older peers may not advertise it.
  displayName?: string;
  // Operator opt-in for public visibility. When false, this node still
  // answers health probes (so direct-IP joins work) but the CF aggregator
  // skips it during the next aggregation cycle. Older peers do not set it;
  // consumers should treat absence as true for backwards compatibility.
  publicListing?: boolean;
}

export interface MetricsResponse {
  address: string;
  publicKey: string;
  serverTime: string;
  displayName?: string;
  publicListing: boolean;
  rooms: number;
  currentBandwidthMbps: number;
  bandwidthCapacityMbps: number | null;
  resolvedCapacityMbps: number | null;
  bandwidthUtilizationRatio?: number;
  capacitySource: string;
  createRoomGuardApplies: boolean;
  createRoomGuardStatus: "allow" | "block" | "unknown";
}
