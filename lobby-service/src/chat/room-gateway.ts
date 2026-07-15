import { createHash, randomUUID } from "node:crypto";
import {
  assertRoomChatWireBudget,
  assertRoomContentContext,
  canonicalizeRoomContent,
  ChatProtocolError,
  deterministicContentJson,
  renderLegacyRoomFallback,
  renderPlainTextFallback,
  type CanonicalChatMessage,
  type ChatContent,
  type ChatProtocolErrorCode,
} from "./protocol.js";
import {
  NO_CHAT_FEATURES,
  PHASE_3_CHAT_FEATURES,
  resolveEnabledFeatures,
  supportsContent,
  type ChatFeatureVersions,
} from "./feature-resolver.js";
import { RateLimitError, SlidingWindowLimiter, TokenBucketLimiter } from "./rate-limiter.js";
import type { RoomChatContext } from "../store.js";

export interface RoomChatPeerRegistration {
  connectionSessionId: string;
  clientIp: string;
  roomId: string;
  roomSessionId: string;
  controlChannelId: string;
  role: "host" | "client";
  authenticatedTicketId?: string;
  send(frame: Record<string, unknown>): void;
  close(code: number, reason: string): void;
}

export interface RoomChatGatewayOptions {
  getRoomChatContext(roomId: string): RoomChatContext | undefined;
  compiledFeatures?: ChatFeatureVersions;
  configuredFeatures?: ChatFeatureVersions;
  adminFeatures?: Partial<ChatFeatureVersions>;
  roomV2Enabled?: boolean;
  now?: () => number;
  randomUuid?: () => string;
  maxPeersTotal?: number;
  maxPeersPerRoom?: number;
  connectionBurst?: number;
  connectionRefillMs?: number;
  ipMessagesPerMinute?: number;
  connectionLimiterMaxKeys?: number;
  ipLimiterMaxKeys?: number;
}

export class RoomChatGatewayError extends Error {
  constructor(
    readonly code: "server_busy",
    message: string,
  ) {
    super(message);
    this.name = "RoomChatGatewayError";
  }
}

export interface LockedRoomChatIdentity {
  playerName: string;
  playerNetId: string;
}

interface RoomChatMessage extends CanonicalChatMessage {
  roomId: string;
  roomSessionId: string;
}

type RoomResult = Record<string, unknown>;

interface DedupeEntry {
  canonicalJson: string;
  result: RoomResult;
}

interface PeerState extends RoomChatPeerRegistration {
  terminal: boolean;
  helloComplete: boolean;
  identity: LockedRoomChatIdentity | null;
  declaredFeatures: ChatFeatureVersions;
  roomV2Capable: boolean;
  dedupe: Map<string, DedupeEntry>;
  dedupeLastSeenAt: number;
}

type RoomRateLimitResult =
  | { allowed: true }
  | { allowed: false; code: "rate_limited"; retryAfterMs: number }
  | { allowed: false; code: "server_busy" };

interface RoomChatV2Envelope {
  clientMessageId: string;
  roomId: string;
  roomSessionId: string;
  content: unknown;
}

const CLIENT_MESSAGE_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const MAX_DEDUPE_ENTRIES = 256;
const DEDUPE_TTL_MS = 10 * 60_000;
const DEFAULT_MAX_PEERS_TOTAL = 500;
const DEFAULT_MAX_PEERS_PER_ROOM = 32;

export class RoomChatGateway {
  private readonly compiledFeatures: ChatFeatureVersions;
  private readonly configuredFeatures: ChatFeatureVersions;
  private readonly adminFeatures?: Partial<ChatFeatureVersions>;
  private readonly roomV2Enabled: boolean;
  private readonly getRoomChatContext: (roomId: string) => RoomChatContext | undefined;
  private readonly now: () => number;
  private readonly randomUuid: () => string;
  private readonly maxPeersTotal: number;
  private readonly maxPeersPerRoom: number;
  private readonly connectionLimiter: TokenBucketLimiter;
  private readonly ipLimiter: SlidingWindowLimiter;
  private readonly peers = new Map<string, PeerState>();
  private readonly peersByRoom = new Map<string, Set<string>>();

  constructor(options: RoomChatGatewayOptions) {
    this.compiledFeatures = { ...(options.compiledFeatures ?? PHASE_3_CHAT_FEATURES) };
    this.configuredFeatures = { ...(options.configuredFeatures ?? PHASE_3_CHAT_FEATURES) };
    if (options.adminFeatures !== undefined) {
      this.adminFeatures = { ...options.adminFeatures };
    }
    this.roomV2Enabled = options.roomV2Enabled ?? true;
    this.getRoomChatContext = options.getRoomChatContext;
    this.now = options.now ?? Date.now;
    this.randomUuid = options.randomUuid ?? randomUUID;
    this.maxPeersTotal = options.maxPeersTotal ?? DEFAULT_MAX_PEERS_TOTAL;
    this.maxPeersPerRoom = options.maxPeersPerRoom ?? DEFAULT_MAX_PEERS_PER_ROOM;
    this.connectionLimiter = new TokenBucketLimiter({
      now: this.now,
      ...(options.connectionBurst === undefined ? {} : { burst: options.connectionBurst }),
      ...(options.connectionRefillMs === undefined ? {} : { refillMs: options.connectionRefillMs }),
      ...(options.connectionLimiterMaxKeys === undefined
        ? {}
        : { maxKeys: options.connectionLimiterMaxKeys }),
    });
    this.ipLimiter = new SlidingWindowLimiter({
      now: this.now,
      purpose: "ip_message",
      ...(options.ipMessagesPerMinute === undefined
        ? {}
        : { maxRequests: options.ipMessagesPerMinute }),
      ...(options.ipLimiterMaxKeys === undefined ? {} : { maxKeys: options.ipLimiterMaxKeys }),
    });
  }

  registerPeer(registration: RoomChatPeerRegistration): void {
    if (this.peers.has(registration.connectionSessionId)) {
      throw new Error("room chat connection session is already registered");
    }
    if (this.peers.size >= this.maxPeersTotal) {
      throw new RoomChatGatewayError("server_busy", "room chat peer capacity exceeded");
    }
    let roomPeers = this.peersByRoom.get(registration.roomId);
    if (roomPeers && roomPeers.size >= this.maxPeersPerRoom) {
      throw new RoomChatGatewayError("server_busy", "room chat room capacity exceeded");
    }
    if (!roomPeers) {
      roomPeers = new Set();
      this.peersByRoom.set(registration.roomId, roomPeers);
    }
    this.peers.set(registration.connectionSessionId, {
      connectionSessionId: registration.connectionSessionId,
      clientIp: registration.clientIp,
      roomId: registration.roomId,
      roomSessionId: registration.roomSessionId,
      controlChannelId: registration.controlChannelId,
      role: registration.role,
      ...(registration.authenticatedTicketId === undefined
        ? {}
        : { authenticatedTicketId: registration.authenticatedTicketId }),
      send: registration.send,
      close: registration.close,
      terminal: false,
      helloComplete: false,
      identity: null,
      declaredFeatures: { ...NO_CHAT_FEATURES },
      roomV2Capable: false,
      dedupe: new Map(),
      dedupeLastSeenAt: this.now(),
    });
    roomPeers.add(registration.connectionSessionId);
  }

  unregisterPeer(connectionSessionId: string): void {
    const peer = this.peers.get(connectionSessionId);
    if (!peer) return;
    this.peers.delete(connectionSessionId);
    this.connectionLimiter.remove(connectionSessionId);
    const roomPeers = this.peersByRoom.get(peer.roomId);
    roomPeers?.delete(connectionSessionId);
    if (roomPeers?.size === 0) this.peersByRoom.delete(peer.roomId);
  }

  close(): void {
    for (const connectionSessionId of [...this.peers.keys()]) {
      this.unregisterPeer(connectionSessionId);
    }
    this.peersByRoom.clear();
    this.ipLimiter.cleanup(Number.MAX_SAFE_INTEGER);
  }

  getLockedIdentity(connectionSessionId: string): LockedRoomChatIdentity | null {
    const identity = this.peers.get(connectionSessionId)?.identity;
    return identity ? { ...identity } : null;
  }

  handleControlEnvelope(
    connectionSessionId: string,
    envelope: unknown,
    rawEnvelope?: string,
  ): boolean {
    const peer = this.peers.get(connectionSessionId);
    if (!peer) {
      return false;
    }
    if (peer.terminal) {
      return true;
    }
    if (!isRecord(envelope) || typeof envelope.type !== "string") {
      return false;
    }
    if (envelope.type === "host_hello" || envelope.type === "client_hello") {
      this.handleHello(peer, envelope);
      return true;
    }
    if (envelope.type === "room_chat_v2") {
      this.handleRoomChatV2(peer, envelope, rawEnvelope);
      return true;
    }
    if (envelope.type === "room_chat") {
      return !this.consumeRoomMessageRate(peer).allowed;
    }
    return false;
  }

  private handleHello(peer: PeerState, envelope: Record<string, unknown>): void {
    if (!Object.hasOwn(envelope, "roomChatVersions")) {
      this.handleLegacyHello(peer, envelope);
      return;
    }

    let identity: LockedRoomChatIdentity;
    let declaredFeatures: ChatFeatureVersions;
    try {
      assertAllowedKeys(envelope, [
        "type",
        "roomId",
        "controlChannelId",
        "role",
        "playerName",
        "playerNetId",
        "roomChatVersions",
      ]);
      assertOwnKeys(envelope, [
        "type",
        "roomId",
        "controlChannelId",
        "role",
        "playerName",
        "playerNetId",
      ]);
      const expectedType = peer.role === "host" ? "host_hello" : "client_hello";
      if (peer.role === "client" && peer.authenticatedTicketId === undefined) {
        throw new RoomEnvelopeError("protocol_mismatch", "client authority is not authenticated");
      }
      if (envelope.type !== expectedType) {
        throw new RoomEnvelopeError("protocol_mismatch", "hello role does not match connection");
      }
      if (envelope.roomId !== peer.roomId) {
        throw new RoomEnvelopeError("protocol_mismatch", "hello room does not match connection");
      }
      if (envelope.controlChannelId !== peer.controlChannelId) {
        throw new RoomEnvelopeError("protocol_mismatch", "hello control channel does not match connection");
      }
      if (envelope.role !== peer.role) {
        throw new RoomEnvelopeError("protocol_mismatch", "hello role does not match connection");
      }
      identity = {
        playerName: normalizePlayerName(envelope.playerName),
        playerNetId: normalizePlayerNetId(envelope.playerNetId),
      };
      declaredFeatures = parseDeclaredFeatures(envelope.roomChatVersions);
    } catch {
      this.rejectHello(peer, true);
      return;
    }

    if (peer.helloComplete && !peer.roomV2Capable) {
      this.rejectHello(peer, true);
      return;
    }
    if (peer.identity) {
      if (
        peer.identity.playerName !== identity.playerName
        || peer.identity.playerNetId !== identity.playerNetId
        || !sameFeatures(peer.declaredFeatures, declaredFeatures)
      ) {
        this.rejectHello(peer, true);
        return;
      }
    }

    const context = this.activeContext(peer);
    if (!context) {
      this.rejectHello(peer, true);
      return;
    }

    if (!peer.identity) {
      peer.helloComplete = true;
      peer.identity = identity;
      peer.declaredFeatures = declaredFeatures;
      peer.roomV2Capable = true;
    }

    peer.send({
      type: "room_chat_ready",
      protocolVersion: 1,
      roomId: peer.roomId,
      roomSessionId: context.roomSessionId,
      enabledFeatures: this.resolveFeatures(peer, peer, context.chatEnabled),
    });
  }

  private handleLegacyHello(peer: PeerState, envelope: Record<string, unknown>): void {
    if (peer.helloComplete) {
      if (peer.roomV2Capable) {
        this.rejectHello(peer, true);
      }
      return;
    }

    let legacyClientIdentity: LockedRoomChatIdentity | null = null;
    try {
      assertOwnKeys(envelope, [
        "type",
        "roomId",
        "controlChannelId",
        "role",
        "playerName",
      ]);
      const expectedType = peer.role === "host" ? "host_hello" : "client_hello";
      if (
        envelope.type !== expectedType
        || envelope.roomId !== peer.roomId
        || envelope.controlChannelId !== peer.controlChannelId
        || envelope.role !== peer.role
      ) {
        return;
      }
      const playerName = normalizePlayerName(envelope.playerName);
      if (peer.role === "client") {
        if (!Object.hasOwn(envelope, "playerNetId")) return;
        legacyClientIdentity = {
          playerName,
          playerNetId: normalizePlayerNetId(envelope.playerNetId),
        };
      } else if (Object.hasOwn(envelope, "playerNetId")) {
        normalizePlayerNetId(envelope.playerNetId);
      }
    } catch {
      return;
    }

    peer.helloComplete = true;
    peer.identity = legacyClientIdentity;
  }

  private handleRoomChatV2(
    peer: PeerState,
    input: Record<string, unknown>,
    rawEnvelope?: string,
  ): void {
    const fallbackClientMessageId = Object.hasOwn(input, "clientMessageId")
      && typeof input.clientMessageId === "string"
      ? input.clientMessageId
      : "";
    if (!peer.identity) {
      this.sendError(peer, "protocol_mismatch", fallbackClientMessageId);
      return;
    }
    if (!peer.roomV2Capable) {
      this.sendError(peer, "protocol_mismatch", fallbackClientMessageId);
      return;
    }

    let envelope: RoomChatV2Envelope;
    try {
      envelope = parseRoomChatV2(input);
    } catch (error) {
      this.sendError(
        peer,
        error instanceof RoomEnvelopeError ? error.code : "invalid_message",
        fallbackClientMessageId,
      );
      return;
    }
    if (envelope.roomId !== peer.roomId || envelope.roomSessionId !== peer.roomSessionId) {
      this.sendError(peer, "protocol_mismatch", envelope.clientMessageId);
      return;
    }
    const receiveContext = this.activeContext(peer, envelope.roomSessionId);
    if (!receiveContext) {
      this.sendError(peer, "protocol_mismatch", envelope.clientMessageId);
      return;
    }
    if (!this.roomV2Enabled) {
      if (!this.activeContext(peer, envelope.roomSessionId)) {
        this.sendError(peer, "protocol_mismatch", envelope.clientMessageId);
        return;
      }
      const dedupeKey = fingerprintUnknown(envelope.content, rawEnvelope);
      if (!this.replayOrRejectConflict(peer, envelope.clientMessageId, dedupeKey)) {
        this.cacheAndSendError(peer, envelope.clientMessageId, dedupeKey, "chat_disabled");
      }
      return;
    }

    let content: ChatContent;
    let canonicalJson: string;
    try {
      const senderFeatures = this.resolveFeatures(peer, peer, true);
      content = canonicalizeRoomContent(envelope.content, {
        richContentVersion: senderFeatures.richContentVersion,
        emojiSetVersion: senderFeatures.emojiSetVersion,
        itemRefVersion: senderFeatures.itemRefVersion,
        combatRefVersion: senderFeatures.combatRefVersion,
      }, {
        envelopeRoomSessionId: envelope.roomSessionId,
        activeRoomSessionId: receiveContext.roomSessionId,
        peerPlayerNetIds: receiveContext.peerPlayerNetIds,
      });
      canonicalJson = deterministicContentJson(content);
    } catch (error) {
      if (!this.activeContext(peer, envelope.roomSessionId)) {
        this.sendError(peer, "protocol_mismatch", envelope.clientMessageId);
        return;
      }
      const dedupeKey = fingerprintUnknown(envelope.content, rawEnvelope);
      if (!this.replayOrRejectConflict(peer, envelope.clientMessageId, dedupeKey)) {
        this.cacheAndSendError(
          peer,
          envelope.clientMessageId,
          dedupeKey,
          error instanceof ChatProtocolError ? error.code : "invalid_content",
        );
      }
      return;
    }

    const commitContext = this.activeContext(peer, envelope.roomSessionId);
    if (!commitContext) {
      this.sendError(peer, "protocol_mismatch", envelope.clientMessageId);
      return;
    }
    try {
      assertRoomContentContext(content, {
        envelopeRoomSessionId: envelope.roomSessionId,
        activeRoomSessionId: commitContext.roomSessionId,
        peerPlayerNetIds: commitContext.peerPlayerNetIds,
      });
    } catch {
      this.sendError(peer, "invalid_content", envelope.clientMessageId);
      return;
    }

    const projectedMessage: RoomChatMessage = {
      roomId: peer.roomId,
      roomSessionId: peer.roomSessionId,
      messageId: "00000000-0000-0000-0000-000000000000",
      senderId: peer.identity.playerNetId,
      senderName: peer.identity.playerName,
      content,
      plainTextFallback: renderPlainTextFallback(content),
      sentAt: "2026-07-12T12:00:00.123Z",
    };
    try {
      assertRoomChatWireBudget(projectedMessage, envelope.clientMessageId);
    } catch {
      if (!this.replayOrRejectConflict(peer, envelope.clientMessageId, canonicalJson)) {
        this.cacheAndSendError(
          peer,
          envelope.clientMessageId,
          canonicalJson,
          "invalid_content",
        );
      }
      return;
    }

    this.expireDedupe(peer);
    if (this.replayOrRejectConflict(peer, envelope.clientMessageId, canonicalJson)) {
      return;
    }
    if (!commitContext.chatEnabled) {
      this.cacheAndSendError(
        peer,
        envelope.clientMessageId,
        canonicalJson,
        "chat_disabled",
      );
      return;
    }

    const rateLimit = this.consumeRoomMessageRate(peer);
    if (!rateLimit.allowed) {
      this.cacheAndSendError(
        peer,
        envelope.clientMessageId,
        canonicalJson,
        rateLimit.code,
        rateLimit.code === "rate_limited" ? rateLimit.retryAfterMs : undefined,
      );
      return;
    }

    const now = this.now();
    const message: RoomChatMessage = {
      ...projectedMessage,
      messageId: this.randomUuid(),
      sentAt: new Date(now).toISOString(),
    };
    const ack = {
      type: "room_chat_ack",
      protocolVersion: 1,
      clientMessageId: envelope.clientMessageId,
      message,
    };
    this.storeDedupe(peer, envelope.clientMessageId, canonicalJson, ack);
    peer.send(ack);

    const roomPeerIds = this.peersByRoom.get(peer.roomId) ?? [];
    for (const recipientId of roomPeerIds) {
      const recipient = this.peers.get(recipientId);
      if (
        !recipient
        || recipient.roomSessionId !== peer.roomSessionId
        || !recipient.helloComplete
      ) {
        continue;
      }
      const recipientFeatures = this.resolveFeatures(peer, recipient, commitContext.chatEnabled);
      if (recipient.roomV2Capable && supportsContent(content, recipientFeatures)) {
        recipient.send({
          type: "room_chat_message",
          protocolVersion: 1,
          message,
        });
      } else {
        recipient.send({
          type: "room_chat",
          roomId: peer.roomId,
          playerName: peer.identity.playerName,
          playerNetId: peer.identity.playerNetId,
          messageId: message.messageId,
          messageText: renderLegacyRoomFallback(content),
          sentAtUnixMs: now,
        });
      }
    }
  }

  private resolveFeatures(
    sender: PeerState,
    receiver: PeerState,
    channelEnabled: boolean,
  ): ChatFeatureVersions {
    return resolveEnabledFeatures({
      channel: "room",
      compiled: this.compiledFeatures,
      configured: this.configuredFeatures,
      ...(this.adminFeatures === undefined ? {} : { admin: this.adminFeatures }),
      channelEnabled,
      roomV2Enabled: this.roomV2Enabled,
      sender: sender.declaredFeatures,
      receiver: receiver.declaredFeatures,
    });
  }

  private activeContext(
    peer: PeerState,
    expectedRoomSessionId = peer.roomSessionId,
  ): RoomChatContext | undefined {
    const context = this.getRoomChatContext(peer.roomId);
    if (
      !context
      || context.roomId !== peer.roomId
      || context.roomSessionId !== peer.roomSessionId
      || context.roomSessionId !== expectedRoomSessionId
    ) {
      return undefined;
    }
    return context;
  }

  private expireDedupe(peer: PeerState): void {
    const now = this.now();
    if (now - peer.dedupeLastSeenAt >= DEDUPE_TTL_MS) {
      peer.dedupe.clear();
    }
    peer.dedupeLastSeenAt = now;
  }

  private storeDedupe(
    peer: PeerState,
    clientMessageId: string,
    canonicalJson: string,
    result: RoomResult,
  ): void {
    peer.dedupeLastSeenAt = this.now();
    peer.dedupe.set(clientMessageId, { canonicalJson, result });
    while (peer.dedupe.size > MAX_DEDUPE_ENTRIES) {
      const oldest = peer.dedupe.keys().next().value;
      if (oldest === undefined) break;
      peer.dedupe.delete(oldest);
    }
  }

  private replayOrRejectConflict(
    peer: PeerState,
    clientMessageId: string,
    canonicalJson: string,
  ): boolean {
    this.expireDedupe(peer);
    const existing = peer.dedupe.get(clientMessageId);
    if (!existing) return false;
    peer.dedupeLastSeenAt = this.now();
    if (existing.canonicalJson === canonicalJson) {
      peer.send(existing.result);
    } else {
      this.sendError(peer, "duplicate_message", clientMessageId);
    }
    return true;
  }

  private cacheAndSendError(
    peer: PeerState,
    clientMessageId: string,
    canonicalJson: string,
    code: ChatProtocolErrorCode,
    retryAfterMs?: number,
  ): void {
    const frame = createErrorFrame(code, clientMessageId, retryAfterMs);
    this.storeDedupe(peer, clientMessageId, canonicalJson, frame);
    peer.send(frame);
  }

  private consumeRoomMessageRate(peer: PeerState): RoomRateLimitResult {
    try {
      const connectionRate = this.connectionLimiter.consume(peer.connectionSessionId);
      if (!connectionRate.allowed) {
        return {
          allowed: false,
          code: "rate_limited",
          retryAfterMs: connectionRate.retryAfterMs,
        };
      }
      const ipRate = this.ipLimiter.consume(peer.clientIp);
      if (!ipRate.allowed) {
        return {
          allowed: false,
          code: "rate_limited",
          retryAfterMs: ipRate.retryAfterMs,
        };
      }
      return { allowed: true };
    } catch (error) {
      if (error instanceof RateLimitError) {
        return { allowed: false, code: "server_busy" };
      }
      throw error;
    }
  }

  private sendError(
    peer: PeerState,
    code: ChatProtocolErrorCode,
    clientMessageId: string,
  ): void {
    peer.send(createErrorFrame(code, clientMessageId));
  }

  private rejectHello(peer: PeerState, close: boolean): void {
    if (peer.terminal) return;
    if (!close) {
      this.sendError(peer, "protocol_mismatch", "");
      return;
    }

    peer.terminal = true;
    try {
      this.sendError(peer, "protocol_mismatch", "");
    } catch {
      // Protocol errors are best effort; the terminal close must still run once.
    }
    try {
      peer.close(1002, "protocol_mismatch");
    } catch {
      // The socket adapter owns transport cleanup after a failed close attempt.
    }
  }
}

class RoomEnvelopeError extends Error {
  constructor(
    readonly code: ChatProtocolErrorCode,
    message: string,
  ) {
    super(message);
  }
}

function parseRoomChatV2(input: Record<string, unknown>): RoomChatV2Envelope {
  const allowed = new Set([
    "type",
    "protocolVersion",
    "clientMessageId",
    "roomId",
    "roomSessionId",
    "content",
  ]);
  if (Object.keys(input).some((key) => !allowed.has(key))) {
    throw new RoomEnvelopeError("invalid_message", "room chat message has unknown fields");
  }
  assertOwnKeys(input, [
    "type",
    "protocolVersion",
    "clientMessageId",
    "roomId",
    "roomSessionId",
    "content",
  ]);
  if (input.type !== "room_chat_v2") {
    throw new RoomEnvelopeError("protocol_mismatch", "room chat type is invalid");
  }
  if (input.protocolVersion !== 1) {
    throw new RoomEnvelopeError("protocol_mismatch", "room chat protocol must be 1");
  }
  if (
    typeof input.clientMessageId !== "string"
    || !CLIENT_MESSAGE_ID.test(input.clientMessageId)
    || typeof input.roomId !== "string"
    || typeof input.roomSessionId !== "string"
  ) {
    throw new RoomEnvelopeError("invalid_message", "room chat message fields are invalid");
  }
  return {
    clientMessageId: input.clientMessageId,
    roomId: input.roomId,
    roomSessionId: input.roomSessionId,
    content: input.content,
  };
}

function parseDeclaredFeatures(input: unknown): ChatFeatureVersions {
  if (!isRecord(input)) {
    throw new Error("roomChatVersions must be an object");
  }
  const allowed = new Set([
    "richContentVersion",
    "emojiSetVersion",
    "itemRefVersion",
    "combatRefVersion",
  ]);
  if (Object.keys(input).some((key) => !allowed.has(key))) {
    throw new Error("roomChatVersions has unknown fields");
  }
  return {
    richContentVersion: parseVersion(Object.hasOwn(input, "richContentVersion")
      ? input.richContentVersion : undefined),
    emojiSetVersion: parseVersion(Object.hasOwn(input, "emojiSetVersion")
      ? input.emojiSetVersion : undefined),
    itemRefVersion: parseVersion(Object.hasOwn(input, "itemRefVersion")
      ? input.itemRefVersion : undefined),
    combatRefVersion: parseVersion(Object.hasOwn(input, "combatRefVersion")
      ? input.combatRefVersion : undefined),
  };
}

function parseVersion(value: unknown): 0 | 1 {
  if (value === undefined) return 0;
  if (value === 0 || value === 1) return value;
  throw new Error("feature version must be 0 or 1");
}

function normalizePlayerName(input: unknown): string {
  if (typeof input !== "string") throw new Error("playerName must be a string");
  assertWellFormedUnicode(input);
  const value = input.normalize("NFC").trim();
  if (Array.from(value).length < 1 || Array.from(value).length > 32) {
    throw new Error("playerName length is invalid");
  }
  for (const ch of value) {
    if (isDisallowedNameChar(ch.codePointAt(0) ?? 0)) {
      throw new Error("playerName contains a disallowed character");
    }
  }
  return value;
}

function normalizePlayerNetId(input: unknown): string {
  if (typeof input !== "string") throw new Error("playerNetId must be a string");
  const value = input.trim();
  if (value.length < 1 || value.length > 128) throw new Error("playerNetId length is invalid");
  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index);
    if (code < 0x20 || code > 0x7e) throw new Error("playerNetId must be printable ASCII");
  }
  return value;
}

function isDisallowedNameChar(code: number): boolean {
  if (code <= 0x1f || code === 0x7f || (code >= 0x80 && code <= 0x9f)) return true;
  if ((code >= 0x202a && code <= 0x202e) || (code >= 0x2066 && code <= 0x206f)) return true;
  if ((code >= 0xfe00 && code <= 0xfe0f) || (code >= 0xe0100 && code <= 0xe01ef)) return true;
  if (code >= 0xe0000 && code <= 0xe007f) return true;
  return [
    0x00ad, 0x061c, 0x180e, 0x200b, 0x200c, 0x200d, 0x200e, 0x200f,
    0x2060, 0x2061, 0x2062, 0x2063, 0x2064, 0xfeff, 0xfff9, 0xfffa, 0xfffb,
  ].includes(code);
}

function sameFeatures(left: ChatFeatureVersions, right: ChatFeatureVersions): boolean {
  return left.richContentVersion === right.richContentVersion
    && left.emojiSetVersion === right.emojiSetVersion
    && left.itemRefVersion === right.itemRefVersion
    && left.combatRefVersion === right.combatRefVersion;
}

function isRecord(input: unknown): input is Record<string, unknown> {
  if (typeof input !== "object" || input === null || Array.isArray(input)) return false;
  try {
    const prototype = Object.getPrototypeOf(input);
    return prototype === Object.prototype || prototype === null;
  } catch {
    return false;
  }
}

function assertOwnKeys(input: Record<string, unknown>, keys: readonly string[]): void {
  for (const key of keys) {
    if (!Object.hasOwn(input, key)) {
      throw new RoomEnvelopeError("invalid_message", `missing required field: ${key}`);
    }
  }
}

function assertAllowedKeys(input: Record<string, unknown>, keys: readonly string[]): void {
  const allowed = new Set(keys);
  if (Object.keys(input).some((key) => !allowed.has(key))) {
    throw new RoomEnvelopeError("protocol_mismatch", "hello has unknown fields");
  }
}

function assertWellFormedUnicode(input: string): void {
  for (let index = 0; index < input.length; index += 1) {
    const code = input.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = input.charCodeAt(index + 1);
      if (!(next >= 0xdc00 && next <= 0xdfff)) {
        throw new Error("string contains an unpaired surrogate");
      }
      index += 1;
    } else if (code >= 0xdc00 && code <= 0xdfff) {
      throw new Error("string contains an unpaired surrogate");
    }
  }
}

function safeErrorMessage(code: ChatProtocolErrorCode): string {
  switch (code) {
    case "invalid_content": return "Chat content is invalid.";
    case "feature_disabled": return "A required chat feature is disabled.";
    case "chat_disabled": return "Room chat is disabled.";
    case "duplicate_message": return "The client message ID was already used.";
    case "protocol_mismatch": return "The room chat protocol or context does not match.";
    case "rate_limited": return "Room chat is temporarily rate limited.";
    case "server_busy": return "Room chat is temporarily busy.";
    default: return "The room chat message was rejected.";
  }
}

function createErrorFrame(
  code: ChatProtocolErrorCode,
  clientMessageId: string,
  retryAfterMs?: number,
): Record<string, unknown> {
  return {
    type: "room_chat_error",
    protocolVersion: 1,
    clientMessageId,
    code,
    message: safeErrorMessage(code),
    ...(retryAfterMs === undefined ? {} : { retryAfterMs }),
  };
}

function fingerprintUnknown(value: unknown, rawEnvelope?: string): string {
  try {
    const stable = stableJsonValue(value, 0, { nodes: 0 });
    return `uncanonical:${createHash("sha256").update(stable).digest("hex")}`;
  } catch {
    if (rawEnvelope !== undefined) {
      return `raw:${createHash("sha256").update(rawEnvelope).digest("hex")}`;
    }
    return "uncanonical:too_complex";
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
