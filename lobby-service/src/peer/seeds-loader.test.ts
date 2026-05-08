import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { loadSeedsFromCf } from "./seeds-loader.js";

test("loadSeedsFromCf returns empty list when CF unreachable", async () => {
  const result = await loadSeedsFromCf("http://127.0.0.1:1");
  assert.deepEqual(result, []);
});

test("loadSeedsFromCf parses CF response correctly", async () => {
  const app = express();
  app.get("/v1/seeds", (_req, res) => res.json({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    seeds: [{ address: "https://a" }, { address: "https://b" }],
  }));
  const server = app.listen(0);
  const port = (server.address() as { port: number }).port;
  const result = await loadSeedsFromCf(`http://127.0.0.1:${port}`);
  server.close();
  assert.deepEqual(result.map((s) => s.address).sort(), ["https://a", "https://b"]);
});
