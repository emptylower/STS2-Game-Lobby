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
}

interface ConnectionState {
  readonly socket: WebSocket;
  readonly sessionId: string;
  readonly senderId: string;
  readonly ticket: ReservedChatTicket;
  snapshotComplete: boolean;
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
    const sessionId = this.randomUuid();
    const state: ConnectionState = {
      socket,
      sessionId,
      senderId: this.randomSenderId(),
      ticket,
      snapshotComplete: false,
    };

    this.startHeartbeat();
    this.peerRegistry.add({ sessionId, clientIp: ticket.clientIp, socket });
    this.connections.set(sessionId, state);
    this.peerRegistry.send(sessionId, {
      type: "chat_ready",
      protocolVersion: 1,
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
    void this.peerRegistry.enqueueSnapshot(sessionId, snapshot).then(() => {
      state.snapshotComplete = true;
    });

    socket.on("message", (data: RawData, isBinary: boolean) => {
      const acceptedAfterSnapshot = state.snapshotComplete;
      this.enqueueEvent(async () => {
        if (!acceptedAfterSnapshot) {
          await this.closeProtocolError(state, "chat snapshot is not complete");
          return;
        }
        await this.handleMessage(state, data, isBinary);
      }, () => {
        this.sendError(state, "server_busy", "chat service is busy");
      });
    });
    socket.on("pong", () => this.peerRegistry.markPong(sessionId));
    socket.once("close", () => {
      this.connections.delete(sessionId);
      this.peerRegistry.remove(sessionId);
      this.connectionLimiter.remove(sessionId);
    });
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
      });
    });
  }

  clearHistory(changedAt = new Date()): void {
    const changedAtIso = changedAt.toISOString();
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
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = null;
    }
    for (const state of this.connections.values()) {
      this.peerRegistry.remove(state.sessionId);
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
    this.heartbeatTimer = setInterval(() => this.peerRegistry.heartbeat(), 5_000);
    this.heartbeatTimer.unref();
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
      await this.closeProtocolError(state, "message must be valid JSON");
      return;
    }

    let envelope: ChatSendEnvelope;
    try {
      envelope = parseChatSend(parsed);
    } catch (error) {
      await this.closeProtocolError(
        state,
        error instanceof Error ? error.message : "invalid chat protocol",
      );
      return;
    }

    let content;
    try {
      content = canonicalizeServerContent(envelope.content);
    } catch (error) {
      if (error instanceof ChatProtocolError) {
        this.sendError(state, error.code, error.message, envelope.clientMessageId);
        return;
      }
      throw error;
    }

    const canonicalJson = deterministicContentJson(content);
    const dedupe = this.dedupe.lookup(state.sessionId, envelope.clientMessageId, canonicalJson);
    if (dedupe.kind === "replay") {
      this.peerRegistry.send(state.sessionId, dedupe.result);
      return;
    }
    if (dedupe.kind === "conflict") {
      this.sendError(
        state,
        "duplicate_message",
        "clientMessageId was already used for different content",
        envelope.clientMessageId,
      );
      return;
    }

    if (!this.chatEnabled) {
      this.sendError(state, "chat_disabled", "server chat is disabled", envelope.clientMessageId);
      return;
    }

    let connectionRate;
    let ipRate;
    try {
      connectionRate = this.connectionLimiter.consume(state.sessionId);
      if (!connectionRate.allowed) {
        this.sendError(
          state,
          "rate_limited",
          "message rate limit exceeded",
          envelope.clientMessageId,
          connectionRate.retryAfterMs,
        );
        return;
      }
      ipRate = this.ipLimiter.consume(state.ticket.clientIp);
    } catch (error) {
      if (error instanceof RateLimitError) {
        this.sendError(state, "server_busy", error.message, envelope.clientMessageId);
        return;
      }
      throw error;
    }
    if (!ipRate.allowed) {
      this.sendError(
        state,
        "rate_limited",
        "IP message rate limit exceeded",
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
        this.sendError(state, error.code, error.message, envelope.clientMessageId);
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
    this.history.append(message);
    try {
      this.dedupe.store(state.sessionId, envelope.clientMessageId, canonicalJson, ack);
    } catch (error) {
      if (error instanceof ChatDedupeError) {
        this.sendError(state, "server_busy", error.message, envelope.clientMessageId);
        return;
      }
      throw error;
    }
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
    message: string,
    clientMessageId?: string,
    retryAfterMs?: number,
  ): void {
    const frame: ChatErrorEnvelope = {
      type: "chat_error",
      protocolVersion: 1,
      clientMessageId: clientMessageId ?? "",
      code,
      message,
      ...(retryAfterMs === undefined ? {} : { retryAfterMs }),
    };
    this.peerRegistry.send(state.sessionId, frame);
    console.log(
      `[chat] event=message_rejected sessionId=${state.sessionId}`
      + ` clientMessageId=${clientMessageId ?? ""} code=${code}`,
    );
  }

  private async closeProtocolError(state: ConnectionState, message: string): Promise<void> {
    const frame: ChatErrorEnvelope = {
      type: "chat_error",
      protocolVersion: 1,
      clientMessageId: "",
      code: "protocol_mismatch",
      message,
    };
    await this.peerRegistry.enqueueSnapshot(state.sessionId, [frame]);
    console.log(
      `[chat] event=protocol_closed sessionId=${state.sessionId} code=1002`,
    );
    state.socket.close(1002, "protocol error");
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
  clientMessageId: string;
  content: unknown;
}

const CLIENT_MESSAGE_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

function parseChatSend(value: unknown): ChatSendEnvelope {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error("message must be an object");
  }
  const record = value as Record<string, unknown>;
  const allowed = new Set(["type", "protocolVersion", "clientMessageId", "content"]);
  if (Object.keys(record).some((key) => !allowed.has(key))) {
    throw new Error("message has unknown fields");
  }
  if (record.type !== "chat_send" || record.protocolVersion !== 1) {
    throw new Error("unsupported chat protocol");
  }
  if (typeof record.clientMessageId !== "string" || !CLIENT_MESSAGE_ID.test(record.clientMessageId)) {
    throw new Error("clientMessageId must be a lowercase UUID");
  }
  return {
    type: "chat_send",
    protocolVersion: 1,
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
