export interface ClientIpRequest {
  socket: {
    remoteAddress?: string | undefined;
  };
  headers: Record<string, string | string[] | undefined>;
}

export function parseTrustedProxyCidrs(value: string | undefined): string[] {
  return (value ?? "").split(",").map((entry) => entry.trim()).filter(Boolean);
}

export function normalizeIp(value: string): string {
  const trimmed = value.trim();
  if (!trimmed) {
    return "";
  }

  const lower = trimmed.toLowerCase();
  if (lower.startsWith("::ffff:")) {
    const mapped = lower.slice("::ffff:".length);
    return normalizeIpv4(mapped);
  }

  const ipv4 = normalizeIpv4(lower);
  if (ipv4) {
    return ipv4;
  }

  return ipv6ToBytes(lower) ? lower : "";
}

export function ipMatchesCidr(ip: string, cidr: string): boolean {
  const normalizedIp = normalizeIp(ip);
  const normalizedCidr = cidr.trim();
  if (!normalizedIp || !normalizedCidr) {
    return false;
  }
  if (normalizedCidr === "*") {
    return true;
  }

  const slashIndex = normalizedCidr.indexOf("/");
  if (slashIndex < 0) {
    return normalizedIp === normalizeIp(normalizedCidr);
  }

  if (slashIndex !== normalizedCidr.lastIndexOf("/")) {
    return false;
  }

  const base = normalizeIp(normalizedCidr.slice(0, slashIndex));
  const prefix = normalizedCidr.slice(slashIndex + 1);
  if (!base || !/^\d+$/.test(prefix)) {
    return false;
  }

  const prefixLength = Number.parseInt(prefix, 10);
  const ipBytes = ipToBytes(normalizedIp);
  const baseBytes = ipToBytes(base);
  if (!ipBytes || !baseBytes || ipBytes.length !== baseBytes.length || prefixLength > ipBytes.length * 8) {
    return false;
  }

  return bytesMatchPrefix(ipBytes, baseBytes, prefixLength);
}

export function resolveClientIp(req: ClientIpRequest, trusted: readonly string[]): string {
  const directPeer = normalizeIp(req.socket.remoteAddress ?? "");
  if (!directPeer || !trusted.some((cidr) => ipMatchesCidr(directPeer, cidr))) {
    return directPeer;
  }

  return resolveForwardedIp(requestHeader(req, "forwarded"))
    ?? resolveXForwardedFor(requestHeader(req, "x-forwarded-for"))
    ?? directPeer;
}

function requestHeader(req: ClientIpRequest, name: string): string | undefined {
  const value = req.headers[name];
  return Array.isArray(value) ? value.join(",") : value;
}

function resolveForwardedIp(header: string | undefined): string | undefined {
  if (!header) {
    return undefined;
  }

  const firstElement = splitHeaderValues(header, ",")[0];
  if (!firstElement) {
    return undefined;
  }

  for (const parameter of splitHeaderValues(firstElement, ";")) {
    const separatorIndex = parameter.indexOf("=");
    if (separatorIndex < 0 || parameter.slice(0, separatorIndex).trim().toLowerCase() !== "for") {
      continue;
    }

    return normalizeForwardedValue(parameter.slice(separatorIndex + 1)) || undefined;
  }

  return undefined;
}

function resolveXForwardedFor(header: string | undefined): string | undefined {
  if (!header) {
    return undefined;
  }

  for (const value of splitHeaderValues(header, ",")) {
    const ip = normalizeForwardedValue(value);
    if (ip) {
      return ip;
    }
  }

  return undefined;
}

function normalizeForwardedValue(value: string): string {
  const unquoted = unquote(value.trim());
  if (!unquoted) {
    return "";
  }

  if (unquoted.startsWith("[")) {
    const endBracket = unquoted.indexOf("]");
    if (endBracket < 0 || !/^(:\d+)?$/.test(unquoted.slice(endBracket + 1))) {
      return "";
    }
    return normalizeIp(unquoted.slice(1, endBracket));
  }

  return normalizeIp(unquoted);
}

function unquote(value: string): string {
  if (!value.startsWith("\"")) {
    return value;
  }
  if (value.length < 2 || !value.endsWith("\"")) {
    return "";
  }

  return value.slice(1, -1).replace(/\\(.)/g, "$1");
}

function splitHeaderValues(value: string, separator: string): string[] {
  const values: string[] = [];
  let start = 0;
  let quoted = false;
  let escaped = false;

  for (let index = 0; index < value.length; index++) {
    const character = value[index]!;
    if (escaped) {
      escaped = false;
      continue;
    }
    if (quoted && character === "\\") {
      escaped = true;
      continue;
    }
    if (character === "\"") {
      quoted = !quoted;
      continue;
    }
    if (!quoted && character === separator) {
      values.push(value.slice(start, index));
      start = index + 1;
    }
  }

  values.push(value.slice(start));
  return values;
}

function ipToBytes(value: string): number[] | null {
  const ipv4 = ipv4ToBytes(value);
  return ipv4 ?? ipv6ToBytes(value);
}

function normalizeIpv4(value: string): string {
  const bytes = ipv4ToBytes(value);
  return bytes ? bytes.join(".") : "";
}

function ipv4ToBytes(value: string): number[] | null {
  const parts = value.split(".");
  if (parts.length !== 4) {
    return null;
  }

  const bytes: number[] = [];
  for (const part of parts) {
    if (!/^\d{1,3}$/.test(part)) {
      return null;
    }

    const parsed = Number.parseInt(part, 10);
    if (parsed > 255) {
      return null;
    }
    bytes.push(parsed);
  }

  return bytes;
}

function ipv6ToBytes(value: string): number[] | null {
  if (!value.includes(":")) {
    return null;
  }

  const doubleColonIndex = value.indexOf("::");
  if (doubleColonIndex !== value.lastIndexOf("::")) {
    return null;
  }

  const expandPart = (part: string) => part.split(":").filter((segment) => segment.length > 0);
  const left = doubleColonIndex >= 0 ? expandPart(value.slice(0, doubleColonIndex)) : expandPart(value);
  const right = doubleColonIndex >= 0 ? expandPart(value.slice(doubleColonIndex + 2)) : [];
  if (doubleColonIndex < 0 && left.length !== 8) {
    return null;
  }

  const missing = doubleColonIndex >= 0 ? 8 - (left.length + right.length) : 0;
  if (missing < 0) {
    return null;
  }

  const groups = [
    ...left,
    ...Array.from({ length: missing }, () => "0"),
    ...right,
  ];
  if (groups.length !== 8) {
    return null;
  }

  const bytes: number[] = [];
  for (const group of groups) {
    if (!/^[0-9a-f]{1,4}$/.test(group)) {
      return null;
    }

    const parsed = Number.parseInt(group, 16);
    bytes.push((parsed >> 8) & 0xff, parsed & 0xff);
  }

  return bytes;
}

function bytesMatchPrefix(ipBytes: number[], baseBytes: number[], prefixLength: number): boolean {
  if (prefixLength <= 0) {
    return true;
  }

  const fullBytes = Math.floor(prefixLength / 8);
  const remainingBits = prefixLength % 8;
  for (let index = 0; index < fullBytes; index++) {
    if (ipBytes[index] !== baseBytes[index]) {
      return false;
    }
  }

  if (remainingBits === 0) {
    return true;
  }

  const mask = (0xff << (8 - remainingBits)) & 0xff;
  return (ipBytes[fullBytes]! & mask) === (baseBytes[fullBytes]! & mask);
}
