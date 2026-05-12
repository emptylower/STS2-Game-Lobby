// lobby-service/src/peer/store.ts
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { dirname } from "node:path";
import type { PeerRecord } from "./types.js";

const PROBE_FAILURE_THRESHOLD = 3;
const TTL_HARD_DELETE_DAYS = 7;

interface PersistedFile {
  version: 1;
  peers: PeerRecord[];
}

export class PeerStore {
  private peers: Map<string, PeerRecord> = new Map();
  private flushPending = false;

  constructor(private readonly path: string) {}

  async load(): Promise<void> {
    if (!existsSync(this.path)) {
      this.peers = new Map();
      return;
    }
    const raw = await readFile(this.path, "utf8");
    const file = JSON.parse(raw) as PersistedFile;
    this.peers = new Map(file.peers.map((p) => [p.address, p]));
  }

  async flush(): Promise<void> {
    await mkdir(dirname(this.path), { recursive: true });
    const file: PersistedFile = { version: 1, peers: [...this.peers.values()] };
    const tmp = `${this.path}.tmp`;
    await writeFile(tmp, JSON.stringify(file, null, 2));
    await rename(tmp, this.path);
    this.flushPending = false;
  }

  scheduleFlush(): void {
    if (this.flushPending) return;
    this.flushPending = true;
    setTimeout(() => { this.flush().catch(() => { this.flushPending = false; }); }, 100);
  }

  list(): PeerRecord[] { return [...this.peers.values()]; }

  get(address: string): PeerRecord | undefined { return this.peers.get(address); }

  async upsert(record: PeerRecord): Promise<void> {
    this.peers.set(record.address, record);
    this.scheduleFlush();
  }

  async heartbeat(address: string, publicKey: string, now = new Date()): Promise<boolean> {
    const existing = this.peers.get(address);
    if (!existing || existing.publicKey !== publicKey) return false;
    existing.lastSeen = now.toISOString();
    existing.status = "active";
    existing.consecutiveProbeFailures = 0;
    this.scheduleFlush();
    return true;
  }

  recordProbeFailure(address: string): void {
    const p = this.peers.get(address);
    if (!p) return;
    p.consecutiveProbeFailures += 1;
    if (p.consecutiveProbeFailures >= PROBE_FAILURE_THRESHOLD) p.status = "offline";
    this.scheduleFlush();
  }

  recordProbeSuccess(address: string, now = new Date()): void {
    const p = this.peers.get(address);
    if (!p) return;
    p.consecutiveProbeFailures = 0;
    p.lastSeen = now.toISOString();
    p.status = "active";
    this.scheduleFlush();
  }

  runTtlCleanup(now = new Date()): number {
    const hardCutoff = now.getTime() - TTL_HARD_DELETE_DAYS * 86400_000;
    let removed = 0;
    for (const [addr, p] of this.peers) {
      if (new Date(p.lastSeen).getTime() < hardCutoff) {
        this.peers.delete(addr);
        removed += 1;
      }
    }
    if (removed > 0) this.scheduleFlush();
    return removed;
  }
}
