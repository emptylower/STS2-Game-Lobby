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
