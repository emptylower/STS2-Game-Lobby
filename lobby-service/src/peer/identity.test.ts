import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadOrCreateIdentity, signChallenge, verifySignature } from "./identity.js";

function tmpDir(): string { return mkdtempSync(join(tmpdir(), "peer-id-")); }

test("loadOrCreateIdentity creates and persists keypair", async () => {
  const dir = tmpDir();
  try {
    const id1 = await loadOrCreateIdentity(dir);
    const id2 = await loadOrCreateIdentity(dir);
    assert.equal(id1.publicKey, id2.publicKey);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("signChallenge produces verifiable signature", async () => {
  const dir = tmpDir();
  try {
    const id = await loadOrCreateIdentity(dir);
    const sig = signChallenge(id, "hello");
    assert.ok(verifySignature(id.publicKey, "hello", sig));
    assert.ok(!verifySignature(id.publicKey, "tampered", sig));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
