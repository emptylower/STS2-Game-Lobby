import { createHash } from "node:crypto";
import { ProtocolContractError } from "./protocol-errors.js";
import type { ProtocolProfile } from "./protocol-profile.js";

export type ProtocolCarrier = "none" | "standalone_tail_v1" | "ritsulib_sidecar_v1";

export interface ProtocolOffer {
  readonly lanProtocolMin: number;
  readonly lanProtocolMax: number;
  readonly clientVersion: string;
  readonly ritsuLibPresent: boolean;
  readonly ritsuLibSidecarAvailable: boolean;
}

export interface ServerProtocolPolicy {
  readonly profile: ProtocolProfile;
  readonly lanProtocolMin: number;
  readonly lanProtocolMax: number;
  readonly maxPlayers: number;
  readonly gameVersion: string;
  readonly wireCacheSignatureV1?: string | undefined;
}

export interface RoomProtocolSelection {
  readonly profile: ProtocolProfile;
  readonly selectedLanProtocolVersion: number;
  readonly carrier: ProtocolCarrier;
  readonly maxPlayers: number;
  readonly minimumClientVersion: string;
  readonly gameVersion: string;
  readonly wireCacheSignature?: string | undefined;
  readonly ritsuLibPresent: boolean;
  readonly capabilityDigest: string;
}

export function selectRoomProtocol(hostOffer: ProtocolOffer, policy: ServerProtocolPolicy): RoomProtocolSelection {
  validateOffer(hostOffer);
  validatePolicy(policy);
  let selectedLanProtocolVersion = 0;
  let carrier: ProtocolCarrier = "none";
  let minimumClientVersion = "0.3.0";

  if (policy.profile === "compat_4_5_v1") {
    if (hostOffer.ritsuLibPresent) {
      throw new ProtocolContractError(
        409,
        "ritsulib_not_allowed_in_compat_mode",
        "兼容模式不支持 RitsuLib。",
        { requiredRitsuLibPresent: false },
      );
    }
  } else {
    selectedLanProtocolVersion = Math.min(hostOffer.lanProtocolMax, policy.lanProtocolMax);
    const commonMinimum = Math.max(hostOffer.lanProtocolMin, policy.lanProtocolMin);
    if (selectedLanProtocolVersion < commonMinimum) {
      throw new ProtocolContractError(
        409,
        "lan_protocol_version_mismatch",
        "客户端与服务器没有共同的 LAN 协议版本。",
      );
    }
    minimumClientVersion = "0.6.0-alpha.1";
    if (hostOffer.ritsuLibPresent) {
      if (!hostOffer.ritsuLibSidecarAvailable) {
        throw sidecarUnavailable(true);
      }
      carrier = "ritsulib_sidecar_v1";
    } else {
      carrier = "standalone_tail_v1";
    }
  }

  const selectionWithoutDigest = {
    profile: policy.profile,
    selectedLanProtocolVersion,
    carrier,
    maxPlayers: policy.maxPlayers,
    minimumClientVersion,
    gameVersion: normalizeBoundedUtf8(policy.gameVersion, "gameVersion", 32),
    ...(policy.wireCacheSignatureV1 === undefined
      ? {}
      : { wireCacheSignature: normalizeSignature(policy.wireCacheSignatureV1) }),
    ritsuLibPresent: hostOffer.ritsuLibPresent,
  } as const;
  return Object.freeze({
    ...selectionWithoutDigest,
    capabilityDigest: computeCapabilityDigest(selectionWithoutDigest),
  });
}

export function assertJoinerCompatible(selection: RoomProtocolSelection, joinerOffer: ProtocolOffer): void {
  validateOffer(joinerOffer);
  if (selection.profile === "compat_4_5_v1") {
    if (joinerOffer.ritsuLibPresent) {
      throw new ProtocolContractError(
        409,
        "ritsulib_not_allowed_in_compat_mode",
        "兼容模式不支持 RitsuLib。",
        { requiredRitsuLibPresent: false },
      );
    }
    return;
  }
  if (joinerOffer.ritsuLibPresent !== selection.ritsuLibPresent) {
    throw new ProtocolContractError(
      409,
      "ritsulib_presence_mismatch",
      "加入者与房间的 RitsuLib 安装状态不一致。",
      { requiredRitsuLibPresent: selection.ritsuLibPresent },
    );
  }
  if (joinerOffer.ritsuLibPresent && !joinerOffer.ritsuLibSidecarAvailable) {
    throw sidecarUnavailable(true);
  }
  if (
    selection.selectedLanProtocolVersion < joinerOffer.lanProtocolMin
    || selection.selectedLanProtocolVersion > joinerOffer.lanProtocolMax
  ) {
    throw new ProtocolContractError(
      409,
      "lan_protocol_version_mismatch",
      "加入者不支持房间冻结的 LAN 协议版本。",
    );
  }
}

export function computeCapabilityDigest(selection: Omit<RoomProtocolSelection, "capabilityDigest">): string {
  const bytes: Buffer[] = [Buffer.from("LANSEL01", "ascii")];
  bytes.push(Buffer.from([
    1,
    selection.profile === "compat_4_5_v1" ? 1 : 2,
    (selection.selectedLanProtocolVersion >>> 8) & 0xff,
    selection.selectedLanProtocolVersion & 0xff,
    selection.carrier === "none" ? 0 : selection.carrier === "standalone_tail_v1" ? 1 : 2,
    selection.maxPlayers,
  ]));
  pushLengthPrefixed(bytes, selection.minimumClientVersion, 32, "minimumClientVersion");
  pushLengthPrefixed(bytes, selection.gameVersion, 32, "gameVersion");
  pushLengthPrefixed(bytes, selection.wireCacheSignature ?? "", 64, "wireCacheSignature", "ascii");
  bytes.push(Buffer.from([selection.ritsuLibPresent ? 1 : 0]));
  return createHash("sha256").update(Buffer.concat(bytes)).digest("hex");
}

export function parseProtocolOffer(value: unknown): ProtocolOffer {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new TypeError("protocolOffer 必须是对象。");
  }
  const candidate = value as Record<string, unknown>;
  const allowed = new Set([
    "lanProtocolMin",
    "lanProtocolMax",
    "clientVersion",
    "ritsuLibPresent",
    "ritsuLibSidecarAvailable",
  ]);
  if (Object.keys(candidate).some((key) => !allowed.has(key))) {
    throw new TypeError("protocolOffer 包含不支持的字段。");
  }
  const offer: ProtocolOffer = {
    lanProtocolMin: requireUint16(candidate.lanProtocolMin, "lanProtocolMin"),
    lanProtocolMax: requireUint16(candidate.lanProtocolMax, "lanProtocolMax"),
    clientVersion: normalizeBoundedUtf8(requireString(candidate.clientVersion, "clientVersion"), "clientVersion", 32),
    ritsuLibPresent: requireBoolean(candidate.ritsuLibPresent, "ritsuLibPresent"),
    ritsuLibSidecarAvailable: requireBoolean(candidate.ritsuLibSidecarAvailable, "ritsuLibSidecarAvailable"),
  };
  validateOffer(offer);
  return Object.freeze(offer);
}

function validateOffer(offer: ProtocolOffer): void {
  requireUint16(offer.lanProtocolMin, "lanProtocolMin");
  requireUint16(offer.lanProtocolMax, "lanProtocolMax");
  normalizeBoundedUtf8(offer.clientVersion, "clientVersion", 32);
  if (offer.lanProtocolMin > offer.lanProtocolMax) throw new TypeError("LAN 协议范围无效。");
  if (!offer.ritsuLibPresent && offer.ritsuLibSidecarAvailable) {
    throw new TypeError("未安装 RitsuLib 时不能声明 sidecar 可用。");
  }
}

function validatePolicy(policy: ServerProtocolPolicy): void {
  if (!Number.isInteger(policy.maxPlayers) || policy.maxPlayers < 2 || policy.maxPlayers > 8) {
    throw new TypeError("maxPlayers 必须是 2-8。");
  }
  requireUint16(policy.lanProtocolMin, "lanProtocolMin");
  requireUint16(policy.lanProtocolMax, "lanProtocolMax");
}

function sidecarUnavailable(requiredPresence: boolean): ProtocolContractError {
  return new ProtocolContractError(409, "ritsulib_sidecar_unavailable", "RitsuLib 已安装，但公开 sidecar 当前不可用。", {
    requiredRitsuLibPresent: requiredPresence,
  });
}

function requireString(value: unknown, name: string): string {
  if (typeof value !== "string") throw new TypeError(`${name} 必须是字符串。`);
  return value;
}

function requireUint16(value: unknown, name: string): number {
  if (typeof value !== "number" || !Number.isInteger(value) || value < 0 || value > 0xffff) {
    throw new TypeError(`${name} 必须是 uint16。`);
  }
  return value;
}

function requireBoolean(value: unknown, name: string): boolean {
  if (typeof value !== "boolean") throw new TypeError(`${name} 必须是布尔值。`);
  return value;
}

function pushLengthPrefixed(
  output: Buffer[],
  value: string,
  maxBytes: number,
  name: string,
  encoding: BufferEncoding = "utf8",
): void {
  const normalized = name === "wireCacheSignature" ? value.toLowerCase() : normalizeBoundedUtf8(value, name, maxBytes);
  const encoded = Buffer.from(normalized, encoding);
  if (encoded.length > maxBytes) throw new TypeError(`${name} 超过 ${maxBytes} bytes。`);
  output.push(Buffer.from([encoded.length]), encoded);
}

function normalizeBoundedUtf8(value: string, name: string, maxBytes: number): string {
  const normalized = value.trim();
  const length = Buffer.byteLength(normalized, "utf8");
  if (length === 0 || length > maxBytes) throw new TypeError(`${name} 必须是 1-${maxBytes} UTF-8 bytes。`);
  return normalized;
}

function normalizeSignature(value: string): string {
  const normalized = value.trim().toLowerCase();
  if (!/^[\x21-\x7e]{1,64}$/.test(normalized)) throw new TypeError("wireCacheSignatureV1 必须是 1-64 bytes ASCII。");
  return normalized;
}
