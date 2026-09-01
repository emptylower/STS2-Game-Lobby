import { createHash } from "node:crypto";
import { ProtocolContractError } from "./protocol-errors.js";
import type { ProtocolProfile } from "./protocol-profile.js";

export type ProtocolCarrier = "none" | "standalone_tail_v1" | "ritsulib_sidecar_v1" | "native_bus_v1";

export interface ProtocolOffer {
  readonly lanProtocolMin: number;
  readonly lanProtocolMax: number;
  readonly clientVersion: string;
  readonly ritsuLibPresent: boolean;
  readonly ritsuLibSidecarAvailable: boolean;
  readonly registryFingerprint?: string | undefined;
  readonly ritsuLibVersion?: string | undefined;
}

/** registry fingerprint 格式：sha256:v1: + 64 位小写 hex。 */
export const RegistryFingerprintPattern = /^sha256:v1:[0-9a-f]{64}$/;

export function isValidRegistryFingerprint(value: string | undefined): value is string {
  return typeof value === "string" && RegistryFingerprintPattern.test(value);
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
  readonly registryFingerprint?: string | undefined;
  readonly ritsuLibVersion?: string | undefined;
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
    minimumClientVersion = "0.6.1-alpha.1";
    // native_bus_v1：tail_v1 一律 native 载体，完全忽略 Ritsu presence 与 sidecar 可用性。
    carrier = "native_bus_v1";
    // 创建侧 fingerprint 必填（房间分配前拒绝，杜绝无冻结指纹房间流入 join 门禁）。
    if (!isValidRegistryFingerprint(hostOffer.registryFingerprint)) {
      throw new ProtocolContractError(
        409,
        "lan_registry_fingerprint_required",
        "创建 tail_v1 房间必须携带格式合法的消息注册表指纹（sha256:v1:<64 位小写 hex>）。",
      );
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
    ...(policy.profile === "compat_4_5_v1" ? {} : {
      registryFingerprint: hostOffer.registryFingerprint,
      ...(hostOffer.ritsuLibVersion === undefined ? {} : { ritsuLibVersion: hostOffer.ritsuLibVersion }),
    }),
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
  if (selection.carrier === "standalone_tail_v1" || selection.carrier === "ritsulib_sidecar_v1") {
    throw new ProtocolContractError(
      409,
      "lan_legacy_carrier_unsupported",
      "该房间使用旧版协议载体，请将房主与全体成员升级 LAN Connect 后重建房间。",
    );
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
    selection.carrier === "none"
      ? 0
      : selection.carrier === "standalone_tail_v1"
        ? 1
        : selection.carrier === "ritsulib_sidecar_v1"
          ? 2
          : 3,
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
    "registryFingerprint",
    "ritsuLibVersion",
  ]);
  if (Object.keys(candidate).some((key) => !allowed.has(key))) {
    throw new TypeError("protocolOffer 包含不支持的字段。");
  }
  // fingerprint 格式不在此处拒绝（避免 400 抢先）：合法性统一由 selectRoomProtocol /
  // join 门禁以 lan_registry_fingerprint_required 拒绝（缺失与格式非法同一码）。
  const registryFingerprint = candidate.registryFingerprint === undefined
    ? undefined
    : boundedRegistryFingerprint(candidate.registryFingerprint);
  const ritsuLibVersion = candidate.ritsuLibVersion === undefined
    ? undefined
    : normalizeBoundedUtf8(requireString(candidate.ritsuLibVersion, "ritsuLibVersion"), "ritsuLibVersion", 32);
  const offer: ProtocolOffer = {
    lanProtocolMin: requireUint16(candidate.lanProtocolMin, "lanProtocolMin"),
    lanProtocolMax: requireUint16(candidate.lanProtocolMax, "lanProtocolMax"),
    clientVersion: normalizeBoundedUtf8(requireString(candidate.clientVersion, "clientVersion"), "clientVersion", 32),
    ritsuLibPresent: requireBoolean(candidate.ritsuLibPresent, "ritsuLibPresent"),
    ritsuLibSidecarAvailable: requireBoolean(candidate.ritsuLibSidecarAvailable, "ritsuLibSidecarAvailable"),
    ...(registryFingerprint === undefined ? {} : { registryFingerprint }),
    ...(ritsuLibVersion === undefined ? {} : { ritsuLibVersion }),
  };
  validateOffer(offer);
  return Object.freeze(offer);
}

function validateOffer(offer: ProtocolOffer): void {
  requireUint16(offer.lanProtocolMin, "lanProtocolMin");
  requireUint16(offer.lanProtocolMax, "lanProtocolMax");
  normalizeBoundedUtf8(offer.clientVersion, "clientVersion", 32);
  if (offer.lanProtocolMin > offer.lanProtocolMax) throw new TypeError("LAN 协议范围无效。");
  // native_bus_v1：sidecar 可用性只是诊断位（0.5.18 事故状态 = present 但不可用，必须照常通过）。
}

function validatePolicy(policy: ServerProtocolPolicy): void {
  if (!Number.isInteger(policy.maxPlayers) || policy.maxPlayers < 2 || policy.maxPlayers > 8) {
    throw new TypeError("maxPlayers 必须是 2-8。");
  }
  requireUint16(policy.lanProtocolMin, "lanProtocolMin");
  requireUint16(policy.lanProtocolMax, "lanProtocolMax");
}

function requireString(value: unknown, name: string): string {
  if (typeof value !== "string") throw new TypeError(`${name} 必须是字符串。`);
  return value;
}

function boundedRegistryFingerprint(value: unknown): string {
  const normalized = requireString(value, "registryFingerprint").trim();
  if (normalized.length === 0 || normalized.length > 96) {
    throw new TypeError("registryFingerprint 必须是 1-96 字节。");
  }

  return normalized;
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
  const normalized = name === "wireCacheSignature" ? value : normalizeBoundedUtf8(value, name, maxBytes);
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
  const normalized = value.trim();
  if (!/^[\x21-\x7e]{1,64}$/.test(normalized)) throw new TypeError("wireCacheSignatureV1 必须是 1-64 bytes ASCII。");
  return normalized;
}
