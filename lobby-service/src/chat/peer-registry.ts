export type ChatPeerErrorCode = "too_many_connections" | "server_busy";

export class ChatPeerError extends Error {
  constructor(
    readonly code: ChatPeerErrorCode,
    message: string,
  ) {
    super(message);
    this.name = "ChatPeerError";
  }
}

export interface ChatSocket {
  readonly readyState: number;
  readonly bufferedAmount: number;
  send(data: string, callback: (error?: Error) => void): void;
  ping(): void;
  close(code: number, reason: string): void;
  terminate(): void;
}

export interface ChatPeer {
  readonly sessionId: string;
  readonly clientIp: string;
  readonly socket: ChatSocket;
}

export interface ChatPeerRegistryOptions {
  now?: () => number;
  setTimeout?: typeof setTimeout;
  clearTimeout?: typeof clearTimeout;
  maxTotal?: number;
  maxPerIp?: number;
  slowClientBytes?: number;
  pingIntervalMs?: number;
  pongTimeoutMs?: number;
  slowClientTerminateMs?: number;
}

const DEFAULT_MAX_TOTAL = 500;
const DEFAULT_MAX_PER_IP = 10;
const DEFAULT_SLOW_CLIENT_BYTES = 262_144;
const DEFAULT_PING_INTERVAL_MS = 30_000;
const DEFAULT_PONG_TIMEOUT_MS = 45_000;
const DEFAULT_SLOW_CLIENT_TERMINATE_MS = 2_000;
const CLOSE_REASON_MAX_BYTES = 123;
const WS_OPEN = 1;

const SLOW_CLIENT_CLOSE_CODE = 1001;
const SLOW_CLIENT_CLOSE_REASON = "slow client";

type QueuedFrame =
  | { kind: "frame"; payload: string; resolve?: () => void; reject?: (error: Error) => void }
  | { kind: "barrier"; resolve: () => void };

interface PeerState {
  peer: ChatPeer;
  queue: QueuedFrame[];
  sending: boolean;
  snapshotBarrier: boolean;
  slow: boolean;
  lastPongAt: number;
  lastPingAt: number;
  terminateTimer: ReturnType<typeof setTimeout> | null;
}

/**
 * Clamp a WebSocket close reason to at most 123 UTF-8 bytes without splitting a
 * Unicode scalar value (code point).
 */
export function clampCloseReason(reason: string): string {
  if (Buffer.byteLength(reason, "utf8") <= CLOSE_REASON_MAX_BYTES) {
    return reason;
  }

  let used = 0;
  let out = "";
  for (const scalar of reason) {
    const bytes = Buffer.byteLength(scalar, "utf8");
    if (used + bytes > CLOSE_REASON_MAX_BYTES) {
      break;
    }
    out += scalar;
    used += bytes;
  }
  return out;
}

export class ChatPeerRegistry {
  private readonly now: () => number;
  private readonly setTimeoutFn: typeof setTimeout;
  private readonly clearTimeoutFn: typeof clearTimeout;
  private readonly maxTotal: number;
  private readonly maxPerIp: number;
  private readonly slowClientBytes: number;
  private readonly pingIntervalMs: number;
  private readonly pongTimeoutMs: number;
  private readonly slowClientTerminateMs: number;
  private readonly peers = new Map<string, PeerState>();
  private readonly ipCounts = new Map<string, number>();

  constructor(options: ChatPeerRegistryOptions = {}) {
    this.now = options.now ?? (() => Date.now());
    this.setTimeoutFn = options.setTimeout ?? setTimeout;
    this.clearTimeoutFn = options.clearTimeout ?? clearTimeout;
    this.maxTotal = options.maxTotal ?? DEFAULT_MAX_TOTAL;
    this.maxPerIp = options.maxPerIp ?? DEFAULT_MAX_PER_IP;
    this.slowClientBytes = options.slowClientBytes ?? DEFAULT_SLOW_CLIENT_BYTES;
    this.pingIntervalMs = options.pingIntervalMs ?? DEFAULT_PING_INTERVAL_MS;
    this.pongTimeoutMs = options.pongTimeoutMs ?? DEFAULT_PONG_TIMEOUT_MS;
    this.slowClientTerminateMs = options.slowClientTerminateMs ?? DEFAULT_SLOW_CLIENT_TERMINATE_MS;
  }

  get size(): number {
    return this.peers.size;
  }

  assertCapacity(clientIp: string): void {
    // Prefer per-IP rejection when both caps are saturated for this address.
    const perIp = this.ipCounts.get(clientIp) ?? 0;
    if (perIp >= this.maxPerIp) {
      throw new ChatPeerError("too_many_connections", "too many chat connections from this IP");
    }
    if (this.peers.size >= this.maxTotal) {
      throw new ChatPeerError("server_busy", "chat connection capacity exceeded");
    }
  }

  add(peer: ChatPeer): void {
    if (this.peers.has(peer.sessionId)) {
      throw new ChatPeerError("server_busy", `chat peer already registered: ${peer.sessionId}`);
    }
    this.assertCapacity(peer.clientIp);
    const now = this.now();
    this.peers.set(peer.sessionId, {
      peer,
      queue: [],
      sending: false,
      snapshotBarrier: false,
      slow: false,
      lastPongAt: now,
      lastPingAt: now,
      terminateTimer: null,
    });
    this.ipCounts.set(peer.clientIp, (this.ipCounts.get(peer.clientIp) ?? 0) + 1);
  }

  remove(sessionId: string): void {
    const state = this.peers.get(sessionId);
    if (!state) {
      return;
    }
    this.peers.delete(sessionId);
    if (state.terminateTimer != null) {
      this.clearTimeoutFn(state.terminateTimer);
      state.terminateTimer = null;
    }
    this.failPending(state, new ChatPeerError("server_busy", "chat peer removed"));
    const count = this.ipCounts.get(state.peer.clientIp) ?? 0;
    if (count <= 1) {
      this.ipCounts.delete(state.peer.clientIp);
    } else {
      this.ipCounts.set(state.peer.clientIp, count - 1);
    }
  }

  enqueueSnapshot(sessionId: string, frames: readonly object[]): Promise<void> {
    const state = this.peers.get(sessionId);
    if (!state) {
      return Promise.reject(new ChatPeerError("server_busy", `unknown chat peer: ${sessionId}`));
    }
    if (state.slow) {
      return Promise.resolve();
    }

    state.snapshotBarrier = true;
    return new Promise<void>((resolve, reject) => {
      for (const frame of frames) {
        if (state.slow) {
          resolve();
          return;
        }
        this.enqueue(state, {
          kind: "frame",
          payload: JSON.stringify(frame),
        });
      }
      this.enqueue(state, {
        kind: "barrier",
        resolve: () => {
          state.snapshotBarrier = false;
          resolve();
          this.pump(state);
        },
      });
      // If enqueue discarded due to slow mid-way, still settle.
      if (state.slow) {
        resolve();
        return;
      }
      // Keep reject available for remove(); attach to last frame if needed.
      void reject;
      this.pump(state);
    });
  }

  send(sessionId: string, frame: object): void {
    const state = this.peers.get(sessionId);
    if (!state || state.slow) {
      return;
    }
    this.enqueue(state, {
      kind: "frame",
      payload: JSON.stringify(frame),
    });
    this.pump(state);
  }

  broadcast(frame: object): void {
    const payload = JSON.stringify(frame);
    for (const state of this.peers.values()) {
      if (state.slow) {
        continue;
      }
      this.enqueue(state, { kind: "frame", payload });
      this.pump(state);
    }
  }

  heartbeat(): void {
    const now = this.now();
    for (const state of this.peers.values()) {
      if (state.peer.socket.readyState !== WS_OPEN) {
        continue;
      }

      if (now - state.lastPongAt >= this.pongTimeoutMs) {
        this.forceTerminate(state);
        continue;
      }

      if (now - state.lastPingAt >= this.pingIntervalMs) {
        try {
          state.peer.socket.ping();
        } catch {
          this.forceTerminate(state);
          continue;
        }
        state.lastPingAt = now;
      }
    }
  }

  markPong(sessionId: string): void {
    const state = this.peers.get(sessionId);
    if (!state) {
      return;
    }
    state.lastPongAt = this.now();
  }

  private enqueue(state: PeerState, item: QueuedFrame): void {
    if (state.slow) {
      return;
    }
    if (this.isSlow(state)) {
      this.markSlow(state);
      return;
    }
    state.queue.push(item);
  }

  private isSlow(state: PeerState): boolean {
    return state.peer.socket.bufferedAmount > this.slowClientBytes;
  }

  private markSlow(state: PeerState): void {
    if (state.slow) {
      return;
    }
    state.slow = true;
    // Discard ordinary queued frames; in-flight send callback still drains sending flag.
    this.failPending(state, new ChatPeerError("server_busy", "slow client"));
    state.queue = [];

    try {
      state.peer.socket.close(SLOW_CLIENT_CLOSE_CODE, clampCloseReason(SLOW_CLIENT_CLOSE_REASON));
    } catch {
      // ignore close failures; terminate path still runs
    }

    if (state.terminateTimer == null) {
      state.terminateTimer = this.setTimeoutFn(() => {
        state.terminateTimer = null;
        this.forceTerminate(state);
      }, this.slowClientTerminateMs);
    }
  }

  private forceTerminate(state: PeerState): void {
    if (state.terminateTimer != null) {
      this.clearTimeoutFn(state.terminateTimer);
      state.terminateTimer = null;
    }
    try {
      state.peer.socket.terminate();
    } catch {
      // ignore
    }
  }

  private failPending(state: PeerState, error: Error): void {
    const pending = state.queue;
    state.queue = [];
    for (const item of pending) {
      if (item.kind === "frame") {
        item.reject?.(error);
      } else {
        // Snapshot barriers should still settle so callers do not hang.
        item.resolve();
      }
    }
  }

  private pump(state: PeerState): void {
    if (state.sending || state.slow) {
      return;
    }

    while (state.queue.length > 0) {
      const next = state.queue[0]!;
      if (next.kind === "barrier") {
        state.queue.shift();
        next.resolve();
        continue;
      }

      if (this.isSlow(state)) {
        this.markSlow(state);
        return;
      }

      if (state.peer.socket.readyState !== WS_OPEN) {
        return;
      }

      state.queue.shift();
      state.sending = true;
      try {
        state.peer.socket.send(next.payload, (error) => {
          state.sending = false;
          if (error) {
            next.reject?.(error);
            this.markSlow(state);
            return;
          }
          next.resolve?.();
          if (this.isSlow(state)) {
            this.markSlow(state);
            return;
          }
          this.pump(state);
        });
      } catch (error) {
        state.sending = false;
        next.reject?.(error instanceof Error ? error : new Error(String(error)));
        this.markSlow(state);
      }
      return;
    }
  }
}
