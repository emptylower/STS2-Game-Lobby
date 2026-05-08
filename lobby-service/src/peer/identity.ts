import { generateKeyPairSync, sign, verify, createPrivateKey, createPublicKey, type KeyObject } from "node:crypto";
import { mkdir, readFile, writeFile, chmod } from "node:fs/promises";
import { existsSync } from "node:fs";
import { dirname, join } from "node:path";

export interface NodeIdentity {
  privateKey: KeyObject;
  publicKey: string;
}

interface IdentityFile {
  version: 1;
  privateKeyPem: string;
  publicKeyPem: string;
}

const FILENAME = "peer-identity.key";

export async function loadOrCreateIdentity(stateDir: string): Promise<NodeIdentity> {
  const path = join(stateDir, FILENAME);
  if (!existsSync(path)) {
    await mkdir(dirname(path), { recursive: true });
    const { privateKey, publicKey } = generateKeyPairSync("ed25519");
    const file: IdentityFile = {
      version: 1,
      privateKeyPem: privateKey.export({ type: "pkcs8", format: "pem" }) as string,
      publicKeyPem: publicKey.export({ type: "spki", format: "pem" }) as string,
    };
    await writeFile(path, JSON.stringify(file));
    await chmod(path, 0o600);
  }
  const raw = await readFile(path, "utf8");
  const file = JSON.parse(raw) as IdentityFile;
  const privateKey = createPrivateKey(file.privateKeyPem);
  const publicKey = base64UrlSpki(file.publicKeyPem);
  return { privateKey, publicKey };
}

export function signChallenge(identity: NodeIdentity, challenge: string): string {
  const sig = sign(null, Buffer.from(challenge, "utf8"), identity.privateKey);
  return sig.toString("base64url");
}

export function verifySignature(publicKeyB64u: string, challenge: string, signatureB64u: string): boolean {
  try {
    const pemDer = Buffer.from(publicKeyB64u, "base64url");
    const pem = `-----BEGIN PUBLIC KEY-----\n${pemDer.toString("base64").match(/.{1,64}/g)!.join("\n")}\n-----END PUBLIC KEY-----\n`;
    const key = createPublicKey(pem);
    return verify(null, Buffer.from(challenge, "utf8"), key, Buffer.from(signatureB64u, "base64url"));
  } catch {
    return false;
  }
}

function base64UrlSpki(pem: string): string {
  const der = pem.replace(/-----BEGIN PUBLIC KEY-----/, "")
    .replace(/-----END PUBLIC KEY-----/, "")
    .replace(/\s+/g, "");
  return Buffer.from(der, "base64").toString("base64url");
}
