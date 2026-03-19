import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { RegistryStore, RegistryStoreError } from "./registry-store.js";

function createStore() {
  const root = mkdtempSync(join(tmpdir(), "sts2-registry-store-"));
  const store = new RegistryStore({
    dataFilePath: join(root, "registry-store.json"),
    officialServer: {
      id: "official-default",
      displayName: "官方测试服",
      regionLabel: "阿里云",
      baseUrl: "http://47.111.146.69:18787",
      wsUrl: "ws://47.111.146.69:18787/control",
      bandwidthProbeUrl: "http://47.111.146.69:18787/probe",
    },
    sessionTtlMs: 60_000,
  });

  return {
    root,
    store,
    cleanup() {
      rmSync(root, { recursive: true, force: true });
    },
  };
}

test("registry store creates submissions and approves them into the public directory", () => {
  const fixture = createStore();
  try {
    const submission = fixture.store.createSubmission({
      displayName: "华东社区服",
      regionLabel: "杭州",
      baseUrl: "http://203.0.113.10:8787",
      bandwidthProbeUrl: "http://203.0.113.10:8787/speed.bin",
      operatorName: "Alice",
      contact: "alice@example.com",
      notes: "晚间稳定",
    }, "198.51.100.12");

    assert.equal(submission.status, "pending");
    assert.equal(fixture.store.listSubmissions().length, 1);

    const approved = fixture.store.approveSubmission(submission.id, "admin", "通过审核");
    assert.equal(approved.sourceType, "community");
    assert.equal(approved.listingState, "approved");

    const publicServers = fixture.store.listPublicServers();
    assert.equal(publicServers.length, 2);
    assert.equal(publicServers[0]?.sourceType, "official");
    assert.equal(publicServers[1]?.displayName, "华东社区服");

    const updatedSubmission = fixture.store.listSubmissions()[0];
    assert.equal(updatedSubmission?.status, "approved");
    assert.equal(updatedSubmission?.linkedServerId, approved.id);
  } finally {
    fixture.cleanup();
  }
});

test("registry store rejects duplicate base urls across submissions and servers", () => {
  const fixture = createStore();
  try {
    fixture.store.createSubmission({
      displayName: "社区服 A",
      regionLabel: "上海",
      baseUrl: "http://203.0.113.11:8787",
      operatorName: "Bob",
      contact: "bob@example.com",
    }, "198.51.100.13");

    assert.throws(
      () =>
        fixture.store.createSubmission({
          displayName: "社区服 B",
          regionLabel: "上海",
          baseUrl: "http://203.0.113.11:8787",
          operatorName: "Carol",
          contact: "carol@example.com",
        }, "198.51.100.14"),
      (error: unknown) => error instanceof RegistryStoreError && error.code === "submission_already_pending",
    );
  } finally {
    fixture.cleanup();
  }
});

test("registry store sessions expire and are cleaned up", () => {
  const fixture = createStore();
  try {
    const createdAt = new Date("2026-03-19T00:00:00.000Z");
    const session = fixture.store.createSession("admin", createdAt);
    assert.equal(fixture.store.getSession(session.id, new Date("2026-03-19T00:00:30.000Z"))?.username, "admin");

    const expired = fixture.store.getSession(session.id, new Date("2026-03-19T00:02:00.000Z"));
    assert.equal(expired, null);
    assert.equal(fixture.store.listAdminServers().length, 1);
  } finally {
    fixture.cleanup();
  }
});
