import { createHash, createHmac, randomBytes, scryptSync, timingSafeEqual } from "node:crypto";

export function hashPassword(password: string) {
  const salt = randomBytes(16).toString("hex");
  const hash = scryptSync(password, salt, 64).toString("hex");
  return `${salt}:${hash}`;
}

export function verifyPassword(password: string, storedHash?: string) {
  if (!storedHash) {
    return false;
  }

  const [salt, expectedHash] = storedHash.split(":");
  if (!salt || !expectedHash) {
    return false;
  }

  const actualHash = scryptSync(password, salt, 64);
  const expected = Buffer.from(expectedHash, "hex");
  return actualHash.length === expected.length && timingSafeEqual(actualHash, expected);
}

export function signSession(sessionId: string, secret: string) {
  const signature = createHmac("sha256", secret)
    .update(sessionId)
    .digest("base64url");
  return `${sessionId}.${signature}`;
}

export function verifySession(token: string | undefined, secret: string) {
  if (!token) {
    return null;
  }

  const dotIndex = token.lastIndexOf(".");
  if (dotIndex <= 0 || dotIndex >= token.length - 1) {
    return null;
  }

  const sessionId = token.slice(0, dotIndex);
  const expectedToken = signSession(sessionId, secret);
  const actual = Buffer.from(token);
  const expected = Buffer.from(expectedToken);
  if (actual.length !== expected.length || !timingSafeEqual(actual, expected)) {
    return null;
  }

  return sessionId;
}

export function hashOpaqueToken(value: string) {
  return createHash("sha256").update(value).digest("hex");
}

export function randomOpaqueToken(bytes = 32) {
  return randomBytes(bytes).toString("base64url");
}

export function deriveServerToken(serverId: string, secret: string) {
  const signature = createHmac("sha256", secret)
    .update(serverId)
    .digest("base64url");
  return `${serverId}.${signature}`;
}
