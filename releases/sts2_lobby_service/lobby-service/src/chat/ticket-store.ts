import {
  createHash,
  createHmac,
  randomBytes as nodeRandomBytes,
  timingSafeEqual,
} from "node:crypto";

export type ChatTicketErrorCode = "invalid_ticket" | "invalid_claims" | "server_busy";

export class ChatTicketError extends Error {
  constructor(
    readonly code: ChatTicketErrorCode,
    message: string,
  ) {
    super(message);
    this.name = "ChatTicketError";
  }
}

export interface ChatTicketClaims {
  protocolVersion: number;
  playerNetId: string;
  playerName: string;
  clientIp: string;
}

export interface ReservedChatTicket {
  id: string;
  protocolVersion: number;
  playerNetId: string;
  playerName: string;
  clientIp: string;
  expiresAt: string;
}

export interface ChatTicketStoreOptions {
  now?: () => number;
  randomBytes?: (size: number) => Buffer;
  hmacSecret?: string | Buffer;
  maxPendingTickets?: number;
  ticketTtlMs?: number;
}

export interface InspectableTicketRecord {
  ticketDigest: string;
  ipDigest: string;
  protocolVersion: number;
  playerNetId: string;
  playerName: string;
  expiresAtMs: number;
  status: "pending" | "reserved";
  reservationId?: string;
}

type TicketStatus = "pending" | "reserved";

interface InternalTicketRecord {
  ticketDigest: Buffer;
  ipDigest: Buffer;
  protocolVersion: number;
  playerNetId: string;
  playerName: string;
  expiresAtMs: number;
  status: TicketStatus;
  reservationId?: string;
}

const DEFAULT_TTL_MS = 60_000;
const DEFAULT_MAX_PENDING = 2000;
const MAX_NAME_SCALARS = 32;
const MAX_NET_ID_ASCII = 128;

function isDisallowedNameChar(ch: string): boolean {
  const code = ch.codePointAt(0);
  if (code === undefined) {
    return true;
  }

  // Names disallow all C0 controls including LF (unlike message text).
  if (code <= 0x1f || code === 0x7f) {
    return true;
  }

  // C1 controls
  if (code >= 0x80 && code <= 0x9f) {
    return true;
  }

  // Bidi overrides / isolates / embeddings
  if ((code >= 0x202a && code <= 0x202e) || (code >= 0x2066 && code <= 0x2069)) {
    return true;
  }

  // Deprecated format controls U+206A..U+206F
  if (code >= 0x206a && code <= 0x206f) {
    return true;
  }

  // Variation selectors
  if ((code >= 0xfe00 && code <= 0xfe0f) || (code >= 0xe0100 && code <= 0xe01ef)) {
    return true;
  }

  // Language tags
  if (code >= 0xe0000 && code <= 0xe007f) {
    return true;
  }

  switch (code) {
    case 0x00ad:
    case 0x061c:
    case 0x180e:
    case 0x200b:
    case 0x200c:
    case 0x200d:
    case 0x200e:
    case 0x200f:
    case 0x2060:
    case 0x2061:
    case 0x2062:
    case 0x2063:
    case 0x2064:
    case 0xfeff:
    case 0xfff9:
    case 0xfffa:
    case 0xfffb:
      return true;
    default:
      return false;
  }
}

function countUnicodeScalars(text: string): number {
  return Array.from(text).length;
}

function isAsciiPrintableNetId(value: string): boolean {
  if (value.length < 1 || value.length > MAX_NET_ID_ASCII) {
    return false;
  }
  for (let i = 0; i < value.length; i += 1) {
    const code = value.charCodeAt(i);
    // Allow printable ASCII 0x20..0x7E (no controls).
    if (code < 0x20 || code > 0x7e) {
      return false;
    }
  }
  return true;
}

function normalizePlayerName(raw: string): string {
  if (typeof raw !== "string") {
    throw new ChatTicketError("invalid_claims", "playerName must be a string");
  }
  const normalized = raw.normalize("NFC").trim();
  const scalars = countUnicodeScalars(normalized);
  if (scalars < 1 || scalars > MAX_NAME_SCALARS) {
    throw new ChatTicketError(
      "invalid_claims",
      `playerName must be 1..${MAX_NAME_SCALARS} Unicode scalars after normalization`,
    );
  }
  for (const ch of normalized) {
    if (isDisallowedNameChar(ch)) {
      const code = ch.codePointAt(0) ?? 0;
      throw new ChatTicketError(
        "invalid_claims",
        `playerName contains disallowed character U+${code.toString(16).padStart(4, "0")}`,
      );
    }
  }
  return normalized;
}

function normalizePlayerNetId(raw: string): string {
  if (typeof raw !== "string") {
    throw new ChatTicketError("invalid_claims", "playerNetId must be a string");
  }
  const normalized = raw.trim();
  if (!isAsciiPrintableNetId(normalized)) {
    throw new ChatTicketError(
      "invalid_claims",
      `playerNetId must be 1..${MAX_NET_ID_ASCII} ASCII characters`,
    );
  }
  return normalized;
}

function assertProtocolVersion(value: number): number {
  if (value !== 1) {
    throw new ChatTicketError("invalid_claims", "protocolVersion must be 1");
  }
  return value;
}

function assertClientIp(value: string): string {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new ChatTicketError("invalid_claims", "clientIp is required");
  }
  return value.trim();
}

function digestTicket(ticket: string): Buffer {
  return createHash("sha256").update(ticket, "utf8").digest();
}

function buffersEqual(a: Buffer, b: Buffer): boolean {
  if (a.length !== b.length) {
    return false;
  }
  return timingSafeEqual(a, b);
}

export class ChatTicketStore {
  private readonly now: () => number;
  private readonly randomBytes: (size: number) => Buffer;
  private readonly hmacSecret: Buffer;
  private readonly maxPendingTickets: number;
  private readonly ticketTtlMs: number;
  private readonly byDigest = new Map<string, InternalTicketRecord>();
  private readonly byReservation = new Map<string, string>();
  private reservationSeq = 0;

  constructor(options: ChatTicketStoreOptions = {}) {
    this.now = options.now ?? Date.now;
    this.randomBytes = options.randomBytes ?? ((size: number) => nodeRandomBytes(size));
    const secret = options.hmacSecret ?? nodeRandomBytes(32);
    this.hmacSecret = typeof secret === "string" ? Buffer.from(secret, "utf8") : secret;
    this.maxPendingTickets = options.maxPendingTickets ?? DEFAULT_MAX_PENDING;
    this.ticketTtlMs = options.ticketTtlMs ?? DEFAULT_TTL_MS;
  }

  issue(claims: ChatTicketClaims): { ticket: string; expiresAt: string } {
    this.cleanup();

    const protocolVersion = assertProtocolVersion(claims.protocolVersion);
    const playerNetId = normalizePlayerNetId(claims.playerNetId);
    const playerName = normalizePlayerName(claims.playerName);
    const clientIp = assertClientIp(claims.clientIp);

    if (this.byDigest.size >= this.maxPendingTickets) {
      throw new ChatTicketError("server_busy", "pending ticket capacity exceeded");
    }

    const ticket = this.randomBytes(32).toString("base64url");
    const ticketDigest = digestTicket(ticket);
    const digestKey = ticketDigest.toString("hex");

    // Extremely unlikely collision with 256-bit digest; still guard.
    if (this.byDigest.has(digestKey)) {
      throw new ChatTicketError("server_busy", "ticket digest collision");
    }

    const expiresAtMs = this.now() + this.ticketTtlMs;
    const record: InternalTicketRecord = {
      ticketDigest,
      ipDigest: this.digestIp(clientIp),
      protocolVersion,
      playerNetId,
      playerName,
      expiresAtMs,
      status: "pending",
    };
    this.byDigest.set(digestKey, record);

    return {
      ticket,
      expiresAt: new Date(expiresAtMs).toISOString(),
    };
  }

  reserve(ticket: string, clientIp: string, protocolVersion: number): ReservedChatTicket {
    if (typeof ticket !== "string" || ticket.length === 0) {
      throw new ChatTicketError("invalid_ticket", "ticket is invalid");
    }

    const ticketDigest = digestTicket(ticket);
    const digestKey = ticketDigest.toString("hex");
    const record = this.byDigest.get(digestKey);

    if (!record || record.status !== "pending") {
      throw new ChatTicketError("invalid_ticket", "ticket is invalid");
    }

    if (record.expiresAtMs <= this.now()) {
      this.deleteRecord(digestKey, record);
      throw new ChatTicketError("invalid_ticket", "ticket is expired");
    }

    if (record.protocolVersion !== protocolVersion) {
      throw new ChatTicketError("invalid_ticket", "protocol version mismatch");
    }

    const ipDigest = this.digestIp(assertClientIp(clientIp));
    if (!buffersEqual(record.ipDigest, ipDigest)) {
      throw new ChatTicketError("invalid_ticket", "client IP mismatch");
    }

    this.reservationSeq += 1;
    const reservationId = `r${this.reservationSeq.toString(36)}_${ticketDigest.subarray(0, 8).toString("hex")}`;
    record.status = "reserved";
    record.reservationId = reservationId;
    this.byReservation.set(reservationId, digestKey);

    return {
      id: reservationId,
      protocolVersion: record.protocolVersion,
      playerNetId: record.playerNetId,
      playerName: record.playerName,
      clientIp: clientIp.trim(),
      expiresAt: new Date(record.expiresAtMs).toISOString(),
    };
  }

  commit(reservationId: string): void {
    const digestKey = this.byReservation.get(reservationId);
    if (!digestKey) {
      throw new ChatTicketError("invalid_ticket", "reservation is invalid");
    }
    const record = this.byDigest.get(digestKey);
    if (!record || record.status !== "reserved" || record.reservationId !== reservationId) {
      throw new ChatTicketError("invalid_ticket", "reservation is invalid");
    }
    // Permanent consumption: remove entirely so ticket cannot be reused.
    this.deleteRecord(digestKey, record);
  }

  release(reservationId: string): void {
    const digestKey = this.byReservation.get(reservationId);
    if (!digestKey) {
      throw new ChatTicketError("invalid_ticket", "reservation is invalid");
    }
    const record = this.byDigest.get(digestKey);
    if (!record || record.status !== "reserved" || record.reservationId !== reservationId) {
      throw new ChatTicketError("invalid_ticket", "reservation is invalid");
    }

    // Return to pending so a later upgrade may reserve again (if not expired).
    this.byReservation.delete(reservationId);
    record.status = "pending";
    delete record.reservationId;
  }

  cleanup(): void {
    const now = this.now();
    for (const [digestKey, record] of this.byDigest) {
      if (record.expiresAtMs <= now) {
        this.deleteRecord(digestKey, record);
      }
    }
  }

  /**
   * Test-only introspection. Never includes raw ticket material.
   */
  inspectForTest(): InspectableTicketRecord[] {
    const out: InspectableTicketRecord[] = [];
    for (const record of this.byDigest.values()) {
      const entry: InspectableTicketRecord = {
        ticketDigest: record.ticketDigest.toString("hex"),
        ipDigest: record.ipDigest.toString("hex"),
        protocolVersion: record.protocolVersion,
        playerNetId: record.playerNetId,
        playerName: record.playerName,
        expiresAtMs: record.expiresAtMs,
        status: record.status,
      };
      if (record.reservationId !== undefined) {
        entry.reservationId = record.reservationId;
      }
      out.push(entry);
    }
    return out;
  }

  private digestIp(clientIp: string): Buffer {
    return createHmac("sha256", this.hmacSecret).update(clientIp, "utf8").digest();
  }

  private deleteRecord(digestKey: string, record: InternalTicketRecord): void {
    if (record.reservationId) {
      this.byReservation.delete(record.reservationId);
    }
    this.byDigest.delete(digestKey);
  }
}
