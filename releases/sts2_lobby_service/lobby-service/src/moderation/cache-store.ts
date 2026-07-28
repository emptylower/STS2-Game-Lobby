import { mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";

interface CacheEntry {
  key: string;
  expiresAt: number;
  lastUsedAt: number;
}

interface PersistedCache {
  version: 1;
  exact: CacheEntry[];
  contexts: CacheEntry[];
}

export interface ModerationCacheStoreOptions {
  now?: () => number;
  exactTtlMs?: number;
  contextTtlMs?: number;
  maxExactEntries?: number;
  maxContextEntries?: number;
}

const DEFAULT_EXACT_TTL_MS = 30 * 24 * 60 * 60_000;
const DEFAULT_CONTEXT_TTL_MS = 7 * 24 * 60 * 60_000;

export class ModerationCacheStore {
  private readonly path: string;
  private readonly now: () => number;
  private readonly exactTtlMs: number;
  private readonly contextTtlMs: number;
  private readonly maxExactEntries: number;
  private readonly maxContextEntries: number;
  private readonly exact = new Map<string, CacheEntry>();
  private readonly contexts = new Map<string, CacheEntry>();
  private dirty = false;

  constructor(path: string, options: ModerationCacheStoreOptions = {}) {
    this.path = resolve(path);
    this.now = options.now ?? Date.now;
    this.exactTtlMs = options.exactTtlMs ?? DEFAULT_EXACT_TTL_MS;
    this.contextTtlMs = options.contextTtlMs ?? DEFAULT_CONTEXT_TTL_MS;
    this.maxExactEntries = options.maxExactEntries ?? 10_000;
    this.maxContextEntries = options.maxContextEntries ?? 20_000;
    this.load();
  }

  hasExact(key: string): boolean {
    return this.has(this.exact, key);
  }

  hasContext(key: string): boolean {
    return this.has(this.contexts, key);
  }

  allowExact(key: string): void {
    this.put(this.exact, key, this.exactTtlMs, this.maxExactEntries);
  }

  allowContext(key: string): void {
    this.put(this.contexts, key, this.contextTtlMs, this.maxContextEntries);
  }

  disallowExact(key: string): void {
    this.remove(this.exact, key);
  }

  disallowContext(key: string): void {
    this.remove(this.contexts, key);
  }

  stats(): { exactEntries: number; contextEntries: number } {
    this.cleanup();
    return { exactEntries: this.exact.size, contextEntries: this.contexts.size };
  }

  flush(): void {
    this.cleanup();
    if (!this.dirty) return;
    mkdirSync(dirname(this.path), { recursive: true });
    const body: PersistedCache = {
      version: 1,
      exact: [...this.exact.values()],
      contexts: [...this.contexts.values()],
    };
    const temp = `${this.path}.tmp`;
    writeFileSync(temp, JSON.stringify(body), { encoding: "utf8", mode: 0o600 });
    renameSync(temp, this.path);
    this.dirty = false;
  }

  private has(entries: Map<string, CacheEntry>, key: string): boolean {
    const entry = entries.get(key);
    if (!entry) return false;
    const now = this.now();
    if (entry.expiresAt <= now) {
      entries.delete(key);
      this.dirty = true;
      return false;
    }
    entry.lastUsedAt = now;
    entries.delete(key);
    entries.set(key, entry);
    this.dirty = true;
    return true;
  }

  private put(entries: Map<string, CacheEntry>, key: string, ttlMs: number, capacity: number): void {
    const now = this.now();
    entries.delete(key);
    entries.set(key, { key, expiresAt: now + ttlMs, lastUsedAt: now });
    while (entries.size > capacity) {
      const oldest = entries.keys().next().value;
      if (oldest === undefined) break;
      entries.delete(oldest);
    }
    this.dirty = true;
    this.flush();
  }

  private remove(entries: Map<string, CacheEntry>, key: string): void {
    if (!entries.delete(key)) return;
    this.dirty = true;
    this.flush();
  }

  private cleanup(): void {
    const now = this.now();
    for (const entries of [this.exact, this.contexts]) {
      for (const [key, entry] of entries) {
        if (entry.expiresAt <= now) {
          entries.delete(key);
          this.dirty = true;
        }
      }
    }
  }

  private load(): void {
    try {
      const data = JSON.parse(readFileSync(this.path, "utf8")) as Partial<PersistedCache>;
      if (data.version !== 1) return;
      this.loadEntries(this.exact, data.exact, this.maxExactEntries);
      this.loadEntries(this.contexts, data.contexts, this.maxContextEntries);
      this.cleanup();
      this.dirty = false;
    } catch {
      // Missing or malformed cache is disposable by design.
    }
  }

  private loadEntries(target: Map<string, CacheEntry>, input: unknown, capacity: number): void {
    if (!Array.isArray(input)) return;
    const valid = input.filter(isCacheEntry)
      .sort((left, right) => left.lastUsedAt - right.lastUsedAt)
      .slice(-capacity);
    for (const entry of valid) target.set(entry.key, { ...entry });
  }
}

function isCacheEntry(value: unknown): value is CacheEntry {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<CacheEntry>;
  return typeof candidate.key === "string"
    && typeof candidate.expiresAt === "number"
    && Number.isFinite(candidate.expiresAt)
    && typeof candidate.lastUsedAt === "number"
    && Number.isFinite(candidate.lastUsedAt);
}
