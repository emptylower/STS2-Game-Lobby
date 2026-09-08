import { ProtocolContractError } from "./protocol-errors.js";

export type ClientApiGeneration = "compat_0_3_0_5" | "canonical_0_6_plus";

export interface ParsedClientVersion {
  readonly canonical: string;
  readonly major: number;
  readonly minor: number;
  readonly patch: number;
  readonly prerelease?: string | undefined;
}

const SemVerPattern = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/;

export function parseClientVersion(value: string): ParsedClientVersion {
  const match = SemVerPattern.exec(value.trim());
  if (!match) {
    throw updateRequired();
  }
  const major = Number(match[1]);
  const minor = Number(match[2]);
  const patch = Number(match[3]);
  const prerelease = match[4];
  return Object.freeze({
    canonical: `${major}.${minor}.${patch}${prerelease === undefined ? "" : `-${prerelease}`}`,
    major,
    minor,
    patch,
    ...(prerelease === undefined ? {} : { prerelease }),
  });
}

export function classifyClientVersion(clientVersion?: string, modVersion?: string): ClientApiGeneration {
  const mod = requireVersion(modVersion);
  const modGeneration = classify(mod);
  if (modGeneration === "canonical_0_6_plus") {
    const client = requireVersion(clientVersion);
    if (client.canonical !== mod.canonical) {
      throw updateRequired();
    }
    return modGeneration;
  }

  if (clientVersion !== undefined) {
    const client = parseClientVersion(clientVersion);
    if (classify(client) !== modGeneration) {
      throw updateRequired();
    }
  }
  return modGeneration;
}

/**
 * semver `>=` 比较（含预发布规则：预发布小于同号正式版；数字标识按数值、字母标识按
 * ASCII；无预发布标签大于有预发布标签）。与客户端 C# 实现共用测试向量。
 */
export function isClientVersionAtLeast(candidate: string, required: string): boolean {
  return compareSemver(parseClientVersion(candidate), parseClientVersion(required)) >= 0;
}

function compareSemver(left: ParsedClientVersion, right: ParsedClientVersion): number {
  if (left.major !== right.major) return left.major - right.major;
  if (left.minor !== right.minor) return left.minor - right.minor;
  if (left.patch !== right.patch) return left.patch - right.patch;
  return comparePrerelease(left.prerelease, right.prerelease);
}

function comparePrerelease(left: string | undefined, right: string | undefined): number {
  if (left === undefined && right === undefined) return 0;
  // 正式版大于任何预发布版本。
  if (left === undefined) return 1;
  if (right === undefined) return -1;
  const leftIdentifiers = left.split(".");
  const rightIdentifiers = right.split(".");
  const count = Math.min(leftIdentifiers.length, rightIdentifiers.length);
  for (let index = 0; index < count; index++) {
    const leftIdentifier = leftIdentifiers[index] as string;
    const rightIdentifier = rightIdentifiers[index] as string;
    const leftNumeric = /^[0-9]+$/.test(leftIdentifier);
    const rightNumeric = /^[0-9]+$/.test(rightIdentifier);
    let comparison: number;
    if (leftNumeric && rightNumeric) {
      comparison = Number(leftIdentifier) - Number(rightIdentifier);
    } else if (leftNumeric) {
      comparison = -1;
    } else if (rightNumeric) {
      comparison = 1;
    } else {
      comparison = leftIdentifier < rightIdentifier ? -1 : leftIdentifier > rightIdentifier ? 1 : 0;
    }
    if (comparison !== 0) return comparison;
  }
  return leftIdentifiers.length - rightIdentifiers.length;
}

export function normalizeClientVersion(clientVersion: string | undefined, modVersion: string): string {
  classifyClientVersion(clientVersion, modVersion);
  return parseClientVersion(clientVersion ?? modVersion).canonical;
}

function requireVersion(value?: string): ParsedClientVersion {
  if (value === undefined || value.trim().length === 0) {
    throw updateRequired();
  }
  return parseClientVersion(value);
}

function classify(version: ParsedClientVersion): ClientApiGeneration {
  if (version.major > 0 || version.minor >= 6) {
    return "canonical_0_6_plus";
  }
  if (version.minor >= 3) {
    return "compat_0_3_0_5";
  }
  throw updateRequired();
}

function updateRequired(): ProtocolContractError {
  return new ProtocolContractError(
    426,
    "client_update_required",
    "LAN Connect 客户端版本过低或版本格式无效，请升级后重试。",
    { requiredClientVersion: "0.3.0" },
  );
}
