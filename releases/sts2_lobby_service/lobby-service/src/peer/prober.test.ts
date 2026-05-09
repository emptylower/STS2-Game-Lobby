import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadOrCreateIdentity } from "./identity.js";
import { mountHealth } from "./handlers/health.js";
import { probeAndVerify } from "./prober.js";

test("probeAndVerify returns publicKey when target signs correctly", async () => {
  const dir = mkdtempSync(join(tmpdir(), "prober-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountHealth(app, { identity, address: "https://self" });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const result = await probeAndVerify(`http://127.0.0.1:${port}`);
    server.close();
    assert.ok(result.ok);
    if (result.ok) assert.equal(result.publicKey, identity.publicKey);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("probeAndVerify fails when target unreachable", async () => {
  const result = await probeAndVerify("http://127.0.0.1:1");
  assert.ok(!result.ok);
});
