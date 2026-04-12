import { createHmac, randomBytes, scryptSync, timingSafeEqual } from "node:crypto";

export function hashServerAdminPassword(password: string) {
  const salt = randomBytes(16).toString("hex");
  const hash = scryptSync(password, salt, 64).toString("hex");
  return `${salt}:${hash}`;
}

export function verifyServerAdminPassword(password: string, storedHash?: string) {
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

export function signServerAdminSession(sessionId: string, secret: string) {
  const signature = createHmac("sha256", secret)
    .update(sessionId)
    .digest("base64url");
  return `${sessionId}.${signature}`;
}

export function verifySignedServerAdminSession(token: string | undefined, secret: string) {
  if (!token) {
    return null;
  }

  const dotIndex = token.lastIndexOf(".");
  if (dotIndex <= 0 || dotIndex >= token.length - 1) {
    return null;
  }

  const sessionId = token.slice(0, dotIndex);
  const expectedToken = signServerAdminSession(sessionId, secret);
  const actual = Buffer.from(token);
  const expected = Buffer.from(expectedToken);
  if (actual.length !== expected.length || !timingSafeEqual(actual, expected)) {
    return null;
  }

  return sessionId;
}
