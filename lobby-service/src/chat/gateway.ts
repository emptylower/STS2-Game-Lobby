import { createHash, randomBytes, randomUUID } from "node:crypto";
import type { RawData, WebSocket } from "ws";
import { ChatDedupeCache, ChatDedupeError, type ChatErrorEnvelope } from "./dedupe-cache.js";
import { ChatHistoryBuffer } from "./history-buffer.js";
import { ChatPeerRegistry } from "./peer-registry.js";
import {
  assertWireBudget,
  canonicalizeServerContent,
  ChatProtocolError,
  deterministicContentJson,
  renderPlainTextFallback,
  type CanonicalChatMessage,
  type ChatProtocolErrorCode,
} from "./protocol.js";
import { RateLimitError, SlidingWindowLimiter, TokenBucketLimiter } from "./rate-limiter.js";
import type { ReservedChatTicket } from "./ticket-store.js";

export interface ServerChatGatewayOptions {
  peerRegistry?: ChatPeerRegistry;
  chatEnabled?: boolean;
  maxPayloadBytes?: number;
  historyLimit?: number;
  historyTtlMs?: number;
  snapshotLimit?: number;
  connectionBurst?: number;
  connectionRefillMs?: number;
  ipMessagesPerMinute?: number;
  now?: () => number;
  randomUuid?: () => string;
  randomSenderId?: () => string;
  protocolErrorCloseGraceMs?: number;
  setInterval?: typeof setInterval;
  clearInterval?: typeof clearInterval;
}

interface ConnectionState {
  readonly socket: WebSocket;
  readonly sessionId: string;
  readonly senderId: string;
  readonly ticket: ReservedChatTicket;
  snapshotComplete: boolean;
  protocolCloseStarted: boolean;
  protocolCloseTimer: NodeJS.Timeout | null;
  protocolCloseFinished: boolean;
  cleanup(): void;
}

const PHASE_ONE_FEATURES = Object.freeze({
  richContentVersion: 0,
  emojiSetVersion: 0,
  itemRefVersion: 0,
});

export class ServerChatGateway {
  private readonly peerRegistry: ChatPeerRegistry;
  private readonly history: ChatHistoryBuffer;
  private readonly maxPayloadBytes: number;
  private readonly now: () => number;
  private readonly randomUuid: () => string;
  private readonly randomSenderId: () => string;
  private readonly protocolErrorCloseGraceMs: number;
  private readonly setIntervalFn: typeof setInterval;
  private readonly clearIntervalFn: typeof clearInterval;
  private readonly dedupe: ChatDedupeCache;
  private readonly connectionLimiter: TokenBucketLimiter;
  private readonly ipLimiter: SlidingWindowLimiter;
  private readonly connections = new Map<string, ConnectionState>();
  private chatEnabled: boolean;
  private desiredChatEnabled: boolean;
  private events: Promise<void> = Promise.resolve();
  private heartbeatTimer: NodeJS.Timeout | null = null;

  constructor(options: ServerChatGatewayOptions = {}) {
    this.peerRegistry = options.peerRegistry ?? new ChatPeerRegistry();
    this.chatEnabled = options.chatEnabled ?? false;
    this.desiredChatEnabled = this.chatEnabled;
    this.maxPayloadBytes = options.maxPayloadBytes ?? 8192;
    this.now = options.now ?? Date.now;
    this.randomUuid = options.randomUuid ?? randomUUID;
    this.randomSenderId = options.randomSenderId ?? (() => randomBytes(16).toString("base64url"));
    this.protocolErrorCloseGraceMs = options.protocolErrorCloseGraceMs ?? 1_000;
    this.setIntervalFn = options.setInterval ?? setInterval;
    this.clearIntervalFn = options.clearInterval ?? clearInterval;
    this.history = new ChatHistoryBuffer({
      now: this.now,
      ...(options.historyLimit === undefined ? {} : { historyLimit: options.historyLimit }),
      ...(options.historyTtlMs === undefined ? {} : { historyTtlMs: options.historyTtlMs }),
      ...(options.snapshotLimit === undefined ? {} : { snapshotLimit: options.snapshotLimit }),
    });
    this.dedupe = new ChatDedupeCache({ now: this.now });
    this.connectionLimiter = new TokenBucketLimiter({
      now: this.now,
      ...(options.connectionBurst === undefined ? {} : { burst: options.connectionBurst }),
      ...(options.connectionRefillMs === undefined ? {} : { refillMs: options.connectionRefillMs }),
    });
    this.ipLimiter = new SlidingWindowLimiter({
      now: this.now,
      purpose: "ip_message",
      ...(options.ipMessagesPerMinute === undefined
        ? {}
        : { maxRequests: options.ipMessagesPerMinute }),
    });
  }

  accept(socket: WebSocket, ticket: ReservedChatTicket): void {
    let state: ConnectionState | null = null;
    let peerAdded = false;
    let cleaned = false;
    let onMessage: ((data: RawData, isBinary: boolean) => void) | null = null;
    let onPong: (() => void) | null = null;

    const cleanup = (): void => {
      if (cleaned) {
        return;
      }
      cleaned = true;
      socket.off("close", cleanup);
      if (onMessage) {
        socket.off("message", onMessage);
      }
      if (onPong) {
        socket.off("pong", onPong);
      }
      if (state) {
        if (state.protocolCloseTimer) {
          clearTimeout(state.protocolCloseTimer);
          state.protocolCloseTimer = null;
        }
        if (this.connections.get(state.sessionId) === state) {
          this.connections.delete(state.sessionId);
        }
        if (peerAdded) {
          this.peerRegistry.remove(state.sessionId);
          peerAdded = false;
        }
        this.connectionLimiter.remove(state.sessionId);
      }
      this.stopHeartbeatIfIdle();
    };

    const failSetup = (): void => {
      cleanup();
      try {
        socket.close(1011, "chat setup failed");
      } catch {
        // terminate below
      }
      try {
        socket.terminate();
      } catch {
        // ignore termination races
      }
    };

    try {
      socket.once("close", cleanup);
      const sessionId = this.randomUuid();
      state = {
        socket,
        sessionId,
        senderId: this.randomSenderId(),
        ticket,
        snapshotComplete: false,
        protocolCloseStarted: false,
        protocolCloseTimer: null,
        protocolCloseFinished: false,
        cleanup,
      };
      this.peerRegistry.add({ sessionId, clientIp: ticket.clientIp, socket });
      peerAdded = true;
      this.connections.set(sessionId, state);
      this.startHeartbeat();

      onMessage = (data: RawData, isBinary: boolean): void => {
        if (state!.protocolCloseStarted) {
          return;
        }
        const acceptedAfterSnapshot = state!.snapshotComplete;
        this.enqueueEvent(async () => {
          if (state!.protocolCloseStarted) {
            return;
          }
          if (!acceptedAfterSnapshot) {
            this.closeProtocolError(state!);
            return;
          }
          await this.handleMessage(state!, data, isBinary);
        }, () => {
          this.sendError(state!, "server_busy");
        });
      };
      onPong = () => this.peerRegistry.markPong(sessionId);
      socket.on("message", onMessage);
      socket.on("pong", onPong);

      this.peerRegistry.send(sessionId, {
        type: "chat_ready",
        protocolVersion: 1,
        channel: "server",
        sessionId,
        senderId: state.senderId,
        instanceId: this.history.instanceId,
        historyEpoch: this.history.historyEpoch,
        chatEnabled: this.desiredChatEnabled,
        serverChatVersion: 1,
        enabledFeatures: PHASE_ONE_FEATURES,
      });

      const snapshotId = this.randomUuid();
      const snapshot = this.history.buildSnapshot(snapshotId, this.maxPayloadBytes);
      const snapshotDelivery = this.peerRegistry.enqueueSnapshot(sessionId, snapshot);
      void snapshotDelivery.then(
        () => {
          if (!cleaned) {
            state!.snapshotComplete = true;
          }
        },
        () => failSetup(),
      );
    } catch (error) {
      failSetup();
      throw error;
    }
  }

  setState(state: { chatEnabled: boolean }): void {
    this.desiredChatEnabled = state.chatEnabled;
    this.enqueueEvent(() => {
      this.chatEnabled = state.chatEnabled;
      this.peerRegistry.broadcast({
        type: "chat_state",
        protocolVersion: 1,
        chatEnabled: this.chatEnabled,
        enabledFeatures: PHASE_ONE_FEATURES,
        historyEpoch: this.history.historyEpoch,
        changedAt: new Date(this.now()).toISOString(),
      });
    });
  }

  clearHistory(changedAt?: Date): void {
    const changedAtIso = (changedAt ?? new Date(this.now())).toISOString();
    this.enqueueEvent(() => {
      const historyEpoch = this.history.clear();
      this.peerRegistry.broadcast({
        type: "chat_history_cleared",
        protocolVersion: 1,
        historyEpoch,
        changedAt: changedAtIso,
      });
    });
  }

  async close(): Promise<void> {
    if (this.heartbeatTimer) {
      this.clearIntervalFn(this.heartbeatTimer);
      this.heartbeatTimer = null;
    }
    for (const state of this.connections.values()) {
      state.cleanup();
      try {
        state.socket.close(1001, "server shutdown");
      } catch {
        state.socket.terminate();
      }
    }
    this.connections.clear();
    await this.events;
  }

  private enqueueEvent(event: () => void | Promise<void>, onError?: () => void): void {
    this.events = this.events.then(event).catch(() => {
      onError?.();
    });
  }

  private startHeartbeat(): void {
    if (this.heartbeatTimer) {
      return;
    }
    this.heartbeatTimer = this.setIntervalFn(() => this.peerRegistry.heartbeat(), 5_000);
    this.heartbeatTimer.unref();
  }

  private stopHeartbeatIfIdle(): void {
    if (this.connections.size !== 0 || !this.heartbeatTimer) {
      return;
    }
    this.clearIntervalFn(this.heartbeatTimer);
    this.heartbeatTimer = null;
  }

  private async handleMessage(
    state: ConnectionState,
    data: RawData,
    isBinary: boolean,
  ): Promise<void> {
    const startedAt = this.now();
    if (isBinary) {
      this.auditClose(state, 1003, rawDataByteLength(data), startedAt);
      state.socket.close(1003, "binary messages are not supported");
      return;
    }

    const raw = rawDataBuffer(data);
    if (raw.byteLength > this.maxPayloadBytes) {
      this.auditClose(state, 1009, raw.byteLength, startedAt);
      state.socket.close(1009, "message too large");
      return;
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(raw));
    } catch (error) {
      if (error instanceof TypeError) {
        this.auditClose(state, 1007, raw.byteLength, startedAt);
        state.socket.close(1007, "invalid UTF-8");
        return;
      }
      this.closeProtocolError(state);
      return;
    }

    let envelope: ChatSendEnvelope;
    try {
      envelope = parseChatSend(parsed);
    } catch {
      this.closeProtocolError(state);
      return;
    }

    let content;
    let canonicalJson: string;
    try {
      content = canonicalizeServerContent(envelope.content);
      canonicalJson = deterministicContentJson(content);
    } catch (error) {
      if (error instanceof ChatProtocolError) {
        const dedupeKey = fingerprintUncanonicalContent(envelope.content, raw);
        if (this.replayOrRejectConflict(state, envelope.clientMessageId, dedupeKey)) {
          return;
        }
        this.cacheAndSendError(
          state,
          dedupeKey,
          error.code,
          envelope.clientMessageId,
        );
        return;
      }
      throw error;
    }

    const dedupeKey = `canonical:${createHash("sha256").update(canonicalJson).digest("hex")}`;
    if (this.replayOrRejectConflict(state, envelope.clientMessageId, dedupeKey)) {
      return;
    }

    if (!this.chatEnabled) {
      this.cacheAndSendError(
        state,
        dedupeKey,
        "chat_disabled",
        envelope.clientMessageId,
      );
      return;
    }

    let connectionRate;
    let ipRate;
    try {
      connectionRate = this.connectionLimiter.consume(state.sessionId);
      if (!connectionRate.allowed) {
        this.cacheAndSendError(
          state,
          dedupeKey,
          "rate_limited",
          envelope.clientMessageId,
          connectionRate.retryAfterMs,
        );
        return;
      }
      ipRate = this.ipLimiter.consume(state.ticket.clientIp);
    } catch (error) {
      if (error instanceof RateLimitError) {
        this.cacheAndSendError(
          state,
          dedupeKey,
          "server_busy",
          envelope.clientMessageId,
        );
        return;
      }
      throw error;
    }
    if (!ipRate.allowed) {
      this.cacheAndSendError(
        state,
        dedupeKey,
        "rate_limited",
        envelope.clientMessageId,
        ipRate.retryAfterMs,
      );
      return;
    }

    const message: CanonicalChatMessage = {
      messageId: this.randomUuid(),
      senderId: state.senderId,
      senderName: state.ticket.playerName,
      content,
      plainTextFallback: renderPlainTextFallback(content),
      sentAt: new Date(this.now()).toISOString(),
    };
    try {
      assertWireBudget(message, this.maxPayloadBytes);
    } catch (error) {
      if (error instanceof ChatProtocolError) {
        this.cacheAndSendError(
          state,
          dedupeKey,
          error.code,
          envelope.clientMessageId,
        );
        return;
      }
      throw error;
    }

    const ack = {
      type: "chat_ack" as const,
      protocolVersion: 1 as const,
      clientMessageId: envelope.clientMessageId,
      message,
    };
    try {
      this.dedupe.store(state.sessionId, envelope.clientMessageId, dedupeKey, ack);
    } catch (error) {
      if (error instanceof ChatDedupeError) {
        this.sendError(state, "server_busy", envelope.clientMessageId);
        return;
      }
      throw error;
    }
    this.history.append(message);
    this.peerRegistry.send(state.sessionId, ack);
    this.peerRegistry.broadcast({
      type: "chat_message",
      protocolVersion: 1,
      message,
    });
    console.log(
      `[chat] event=message_accepted sessionId=${state.sessionId}`
      + ` messageId=${message.messageId} clientMessageId=${envelope.clientMessageId}`
      + ` contentHash=${createHash("sha256").update(canonicalJson).digest("hex")}`
      + ` segments=${content.segments.length} bytes=${raw.byteLength}`
      + ` durationMs=${Math.max(0, this.now() - startedAt)}`,
    );
  }

  private sendError(
    state: ConnectionState,
    code: ChatProtocolErrorCode,
    clientMessageId?: string,
    retryAfterMs?: number,
  ): void {
    const frame: ChatErrorEnvelope = {
      type: "chat_error",
      protocolVersion: 1,
      clientMessageId: clientMessageId ?? "",
      code,
      message: safeChatErrorMessage(code),
      ...(retryAfterMs === undefined ? {} : { retryAfterMs }),
    };
    this.peerRegistry.send(state.sessionId, frame);
    console.log(
      `[chat] event=message_rejected sessionId=${state.sessionId}`
      + ` clientMessageId=${clientMessageId ?? ""} code=${code}`,
    );
  }

  private replayOrRejectConflict(
    state: ConnectionState,
    clientMessageId: string,
    dedupeKey: string,
  ): boolean {
    const dedupe = this.dedupe.lookup(state.sessionId, clientMessageId, dedupeKey);
    if (dedupe.kind === "replay") {
      this.peerRegistry.send(state.sessionId, dedupe.result);
      return true;
    }
    if (dedupe.kind === "conflict") {
      this.sendError(
        state,
        "duplicate_message",
        clientMessageId,
      );
      return true;
    }
    return false;
  }

  private cacheAndSendError(
    state: ConnectionState,
    dedupeKey: string,
    code: ChatProtocolErrorCode,
    clientMessageId: string,
    retryAfterMs?: number,
  ): void {
    const frame: ChatErrorEnvelope = {
      type: "chat_error",
      protocolVersion: 1,
      clientMessageId,
      code,
      message: safeChatErrorMessage(code),
      ...(retryAfterMs === undefined ? {} : { retryAfterMs }),
    };
    try {
      this.dedupe.store(state.sessionId, clientMessageId, dedupeKey, frame);
    } catch (error) {
      if (error instanceof ChatDedupeError) {
        this.sendError(state, "server_busy", clientMessageId);
        return;
      }
      throw error;
    }
    this.peerRegistry.send(state.sessionId, frame);
    console.log(
      `[chat] event=message_rejected sessionId=${state.sessionId}`
      + ` clientMessageId=${clientMessageId} code=${code}`,
    );
  }

  private closeProtocolError(state: ConnectionState): void {
    if (state.protocolCloseStarted) {
      return;
    }
    state.protocolCloseStarted = true;
    const frame: ChatErrorEnvelope = {
      type: "chat_error",
      protocolVersion: 1,
      clientMessageId: "",
      code: "protocol_mismatch",
      message: safeChatErrorMessage("protocol_mismatch"),
    };
    let delivery: Promise<void>;
    try {
      delivery = this.peerRegistry.enqueueSnapshot(state.sessionId, [frame]);
    } catch {
      this.finishProtocolClose(state, true);
      return;
    }

    state.protocolCloseTimer = setTimeout(() => {
      state.protocolCloseTimer = null;
      this.finishProtocolClose(state, true);
    }, this.protocolErrorCloseGraceMs);
    state.protocolCloseTimer.unref();
    void delivery.then(
      () => this.finishProtocolClose(state, false),
      () => this.finishProtocolClose(state, true),
    );
  }

  private finishProtocolClose(state: ConnectionState, terminate: boolean): void {
    if (state.protocolCloseFinished || this.connections.get(state.sessionId) !== state) {
      return;
    }
    state.protocolCloseFinished = true;
    if (state.protocolCloseTimer) {
      clearTimeout(state.protocolCloseTimer);
      state.protocolCloseTimer = null;
    }
    console.log(`[chat] event=protocol_closed sessionId=${state.sessionId} code=1002`);
    try {
      state.socket.close(1002, "protocol error");
    } catch {
      terminate = true;
    }
    if (terminate) {
      try {
        state.socket.terminate();
      } catch {
        // ignore termination races
      }
    }
  }

  private auditClose(
    state: ConnectionState,
    code: number,
    bytes: number,
    startedAt: number,
  ): void {
    console.log(
      `[chat] event=transport_closed sessionId=${state.sessionId}`
      + ` code=${code} bytes=${bytes} durationMs=${Math.max(0, this.now() - startedAt)}`,
    );
  }
}

interface ChatSendEnvelope {
  type: "chat_send";
  protocolVersion: 1;
  channel: "server";
  clientMessageId: string;
  content: unknown;
}

const CLIENT_MESSAGE_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

function parseChatSend(value: unknown): ChatSendEnvelope {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error("message must be an object");
  }
  const record = value as Record<string, unknown>;
  const allowed = new Set(["type", "protocolVersion", "channel", "clientMessageId", "content"]);
  if (Object.keys(record).some((key) => !allowed.has(key))) {
    throw new Error("message has unknown fields");
  }
  if (record.type !== "chat_send" || record.protocolVersion !== 1) {
    throw new Error("unsupported chat protocol");
  }
  if (record.channel !== "server") {
    throw new Error("channel must be server");
  }
  if (typeof record.clientMessageId !== "string" || !CLIENT_MESSAGE_ID.test(record.clientMessageId)) {
    throw new Error("clientMessageId must be a lowercase UUID");
  }
  return {
    type: "chat_send",
    protocolVersion: 1,
    channel: "server",
    clientMessageId: record.clientMessageId,
    content: record.content,
  };
}

function rawDataBuffer(data: RawData): Buffer {
  if (typeof data === "string") {
    return Buffer.from(data, "utf8");
  }
  if (Buffer.isBuffer(data)) {
    return data;
  }
  if (data instanceof ArrayBuffer) {
    return Buffer.from(data);
  }
  if (Array.isArray(data)) {
    return Buffer.concat(data);
  }
  throw new TypeError("unsupported websocket payload");
}

function rawDataByteLength(data: RawData): number {
  if (typeof data === "string") {
    return Buffer.byteLength(data, "utf8");
  }
  if (Buffer.isBuffer(data) || data instanceof ArrayBuffer) {
    return data.byteLength;
  }
  return data.reduce((total, part) => total + part.byteLength, 0);
}

function fingerprintUncanonicalContent(content: unknown, raw: Buffer): string {
  try {
    const stable = stableJsonValue(content, 0, { nodes: 0 });
    return `uncanonical:${createHash("sha256").update(stable).digest("hex")}`;
  } catch {
    return `raw:${createHash("sha256").update(raw).digest("hex")}`;
  }
}

function safeChatErrorMessage(code: ChatProtocolErrorCode): string {
  switch (code) {
    case "invalid_content":
      return "invalid chat content";
    case "feature_disabled":
      return "requested chat feature is disabled";
    case "invalid_message":
      return "invalid chat message";
    case "chat_disabled":
      return "server chat is disabled";
    case "rate_limited":
      return "message rate limit exceeded";
    case "duplicate_message":
      return "clientMessageId was already used for different content";
    case "protocol_mismatch":
      return "chat protocol mismatch";
    case "server_busy":
      return "chat service is busy";
  }
}

function stableJsonValue(value: unknown, depth: number, budget: { nodes: number }): string {
  budget.nodes += 1;
  if (depth > 64 || budget.nodes > 4_096) {
    throw new RangeError("content fingerprint complexity exceeded");
  }
  if (value === null || typeof value !== "object") {
    return JSON.stringify(value) ?? "undefined";
  }
  if (Array.isArray(value)) {
    return `[${value.map((entry) => stableJsonValue(entry, depth + 1, budget)).join(",")}]`;
  }
  const record = value as Record<string, unknown>;
  return `{${Object.keys(record)
    .sort()
    .map((key) => `${JSON.stringify(key)}:${stableJsonValue(record[key], depth + 1, budget)}`)
    .join(",")}}`;
}
