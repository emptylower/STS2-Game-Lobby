import { createCipheriv, createDecipheriv, createHmac, hkdfSync, randomBytes } from "node:crypto";

export interface EncryptedValue {
  algorithm: "aes-256-gcm";
  iv: string;
  ciphertext: string;
  tag: string;
}

export function parseModerationMasterKey(value: string | undefined): Buffer | null {
  const normalized = value?.trim();
  if (!normalized) return null;
  if (/^[0-9a-fA-F]{64}$/.test(normalized)) {
    return Buffer.from(normalized, "hex");
  }
  try {
    const decoded = Buffer.from(normalized, "base64");
    return decoded.length === 32 ? decoded : null;
  } catch {
    return null;
  }
}

export function encryptModerationValue(
  key: Buffer,
  value: string,
  aad: string,
): EncryptedValue {
  const iv = randomBytes(12);
  const cipher = createCipheriv("aes-256-gcm", key, iv);
  cipher.setAAD(Buffer.from(aad, "utf8"));
  const ciphertext = Buffer.concat([cipher.update(value, "utf8"), cipher.final()]);
  return {
    algorithm: "aes-256-gcm",
    iv: iv.toString("base64url"),
    ciphertext: ciphertext.toString("base64url"),
    tag: cipher.getAuthTag().toString("base64url"),
  };
}

export function decryptModerationValue(
  key: Buffer,
  value: EncryptedValue,
  aad: string,
): string {
  if (value.algorithm !== "aes-256-gcm") throw new Error("unsupported encryption algorithm");
  const decipher = createDecipheriv("aes-256-gcm", key, Buffer.from(value.iv, "base64url"));
  decipher.setAAD(Buffer.from(aad, "utf8"));
  decipher.setAuthTag(Buffer.from(value.tag, "base64url"));
  return Buffer.concat([
    decipher.update(Buffer.from(value.ciphertext, "base64url")),
    decipher.final(),
  ]).toString("utf8");
}

export function deriveModerationHmacKey(masterKey: Buffer): Buffer {
  return Buffer.from(hkdfSync("sha256", masterKey, Buffer.alloc(0), "sts2-ai-moderation-hmac-v1", 32));
}

export function moderationHmac(key: Buffer, domain: string, value: string): string {
  return createHmac("sha256", key).update(domain).update("\0").update(value).digest("base64url");
}
