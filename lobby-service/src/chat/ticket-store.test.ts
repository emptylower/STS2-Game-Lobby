import assert from "node:assert/strict";
import test from "node:test";
import {
  ChatTicketStore,
  type ChatTicketStoreOptions,
  type InspectableTicketRecord,
} from "./ticket-store.js";

function hasCode(code: string) {
  return (error: unknown) =>
    error instanceof Error &&
    "code" in error &&
    (error as { code: unknown }).code === code;
}

function makeStore(overrides: Partial<ChatTicketStoreOptions> = {}) {
  let nowMs = 1_000_000;
  let randomCall = 0;
  const options: ChatTicketStoreOptions = {
    now: () => nowMs,
    randomBytes: (size: number) => {
      // Unique per call so capacity tests do not collide digests.
      const buf = Buffer.alloc(size);
      buf.writeUInt32BE(randomCall >>> 0, 0);
      buf.fill((randomCall + 7) & 0xff, 4);
      randomCall += 1;
      return buf;
    },
    hmacSecret: "test-hmac-secret",
    maxPendingTickets: 2000,
    ticketTtlMs: 60_000,
    ...overrides,
  };
  const store = new ChatTicketStore(options);
  return {
    store,
    setNow: (ms: number) => {
      nowMs = ms;
    },
    advance: (ms: number) => {
      nowMs += ms;
    },
    options,
  };
}

function baseClaims(overrides: Partial<{
  protocolVersion: number;
  playerNetId: string;
  playerName: string;
  clientIp: string;
}> = {}) {
  return {
    protocolVersion: 1,
    playerNetId: "net-1",
    playerName: "Ironclad",
    clientIp: "203.0.113.4",
    ...overrides,
  };
}

test("ticket is IP-bound and commits once", () => {
  const { store } = makeStore({
    now: () => 1_000_000,
    randomBytes: () => Buffer.alloc(32, 7),
  });
  const issued = store.issue(baseClaims());
  assert.equal(typeof issued.ticket, "string");
  assert.ok(issued.ticket.length > 0);
  assert.equal(issued.expiresAt, new Date(1_000_000 + 60_000).toISOString());
  assert.equal(
    store.inspectForTest().some((record) => "ticket" in record),
    false,
  );
  const reservation = store.reserve(issued.ticket, "203.0.113.4", 1);
  assert.equal(reservation.playerName, "Ironclad");
  assert.equal(reservation.playerNetId, "net-1");
  assert.equal(reservation.protocolVersion, 1);
  store.commit(reservation.id);
  assert.throws(
    () => store.reserve(issued.ticket, "203.0.113.4", 1),
    hasCode("invalid_ticket"),
  );
});

test("rejects wrong IP and protocol version", () => {
  const { store } = makeStore({ randomBytes: () => Buffer.alloc(32, 9) });
  const issued = store.issue(baseClaims());
  assert.throws(
    () => store.reserve(issued.ticket, "198.51.100.7", 1),
    hasCode("invalid_ticket"),
  );
  assert.throws(
    () => store.reserve(issued.ticket, "203.0.113.4", 2),
    hasCode("invalid_ticket"),
  );
  // Original ticket still redeemable with correct binding.
  const reservation = store.reserve(issued.ticket, "203.0.113.4", 1);
  assert.equal(reservation.playerNetId, "net-1");
});

test("release after failed upgrade allows a later reserve", () => {
  const { store } = makeStore({ randomBytes: () => Buffer.alloc(32, 11) });
  const issued = store.issue(baseClaims());
  const first = store.reserve(issued.ticket, "203.0.113.4", 1);
  assert.throws(
    () => store.reserve(issued.ticket, "203.0.113.4", 1),
    hasCode("invalid_ticket"),
  );
  store.release(first.id);
  const second = store.reserve(issued.ticket, "203.0.113.4", 1);
  assert.notEqual(second.id, first.id);
  store.commit(second.id);
  assert.throws(
    () => store.reserve(issued.ticket, "203.0.113.4", 1),
    hasCode("invalid_ticket"),
  );
});

test("expires after 60 seconds", () => {
  const { store, setNow } = makeStore({
    randomBytes: () => Buffer.alloc(32, 13),
  });
  const issued = store.issue(baseClaims());
  setNow(1_000_000 + 60_000);
  assert.throws(
    () => store.reserve(issued.ticket, "203.0.113.4", 1),
    hasCode("invalid_ticket"),
  );
});

test("cleanup removes expired pending tickets", () => {
  const { store, setNow } = makeStore();
  store.issue(baseClaims({ playerNetId: "a" }));
  store.issue(baseClaims({ playerNetId: "b" }));
  assert.equal(store.inspectForTest().length, 2);
  setNow(1_000_000 + 60_001);
  store.cleanup();
  assert.equal(store.inspectForTest().length, 0);
});

test("enforces 2000 capacity with server_busy", () => {
  const { store } = makeStore({ maxPendingTickets: 2000 });
  for (let i = 0; i < 2000; i += 1) {
    store.issue(baseClaims({ playerNetId: `net-${i}` }));
  }
  assert.equal(store.inspectForTest().length, 2000);
  assert.throws(
    () => store.issue(baseClaims({ playerNetId: "overflow" })),
    hasCode("server_busy"),
  );
});

test("stores only digests, never raw tickets", () => {
  const fixed = Buffer.alloc(32, 42);
  const { store } = makeStore({ randomBytes: () => Buffer.from(fixed) });
  const issued = store.issue(baseClaims());
  const records = store.inspectForTest();
  assert.equal(records.length, 1);
  const record = records[0] as InspectableTicketRecord;
  assert.equal("ticket" in record, false);
  assert.equal(typeof record.ticketDigest, "string");
  assert.ok(record.ticketDigest.length > 0);
  assert.equal(typeof record.ipDigest, "string");
  assert.ok(record.ipDigest.length > 0);
  // Raw ticket material must not appear in any inspect field.
  const serialized = JSON.stringify(records);
  assert.equal(serialized.includes(issued.ticket), false);
  assert.equal(record.status, "pending");
});

test("inspectForTest never returns raw ticket field", () => {
  const { store } = makeStore();
  const issued = store.issue(baseClaims());
  const reserved = store.reserve(issued.ticket, "203.0.113.4", 1);
  for (const record of store.inspectForTest()) {
    assert.equal(Object.hasOwn(record, "ticket"), false);
    assert.ok("ticketDigest" in record);
  }
  store.commit(reserved.id);
  assert.equal(store.inspectForTest().length, 0);
});

test("validates playerName 1..32 Unicode scalars and denylist", () => {
  const { store } = makeStore();

  assert.throws(() => store.issue(baseClaims({ playerName: "" })), hasCode("invalid_claims"));
  assert.throws(
    () => store.issue(baseClaims({ playerName: "a".repeat(33) })),
    hasCode("invalid_claims"),
  );
  // C0 control
  assert.throws(
    () => store.issue(baseClaims({ playerName: "bad\nname" })),
    hasCode("invalid_claims"),
  );
  // bidi override
  assert.throws(
    () => store.issue(baseClaims({ playerName: `x\u202Ey` })),
    hasCode("invalid_claims"),
  );
  // zero-width space
  assert.throws(
    () => store.issue(baseClaims({ playerName: `a\u200Bb` })),
    hasCode("invalid_claims"),
  );
  // BOM / ZWNBSP inside name (leading FEFF is stripped by trim)
  assert.throws(
    () => store.issue(baseClaims({ playerName: "na\uFEFFme" })),
    hasCode("invalid_claims"),
  );

  // NFC combining mark counts as composed scalar after normalize
  const issued = store.issue(baseClaims({ playerName: "e\u0301clair" }));
  const reservation = store.reserve(issued.ticket, "203.0.113.4", 1);
  assert.equal(reservation.playerName, "éclair");

  // 32 scalars ok (emoji is one scalar)
  const emojiName = "😀".repeat(32);
  const issued2 = store.issue(
    baseClaims({ playerName: emojiName, playerNetId: "net-emoji" }),
  );
  assert.ok(issued2.ticket);
});

test("validates playerNetId 1..128 ASCII", () => {
  const { store } = makeStore();
  assert.throws(() => store.issue(baseClaims({ playerNetId: "" })), hasCode("invalid_claims"));
  assert.throws(
    () => store.issue(baseClaims({ playerNetId: "x".repeat(129) })),
    hasCode("invalid_claims"),
  );
  // non-ASCII
  assert.throws(
    () => store.issue(baseClaims({ playerNetId: "net-名字" })),
    hasCode("invalid_claims"),
  );
  const issued = store.issue(baseClaims({ playerNetId: "a".repeat(128) }));
  assert.ok(issued.ticket);
});

test("rejects unknown ticket material", () => {
  const { store } = makeStore();
  assert.throws(
    () => store.reserve("not-a-real-ticket", "203.0.113.4", 1),
    hasCode("invalid_ticket"),
  );
});

test("commit and release on unknown reservation are safe no-ops or errors", () => {
  const { store } = makeStore();
  // Unknown commit/release should not throw hard, or throw invalid_ticket consistently.
  // Spec: only commit consumes permanently; release after failed upgrade.
  assert.throws(() => store.commit("missing"), hasCode("invalid_ticket"));
  assert.throws(() => store.release("missing"), hasCode("invalid_ticket"));
});

test("ticket bytes are base64url from 32 random bytes", () => {
  const fixed = Buffer.alloc(32, 0xab);
  const { store } = makeStore({ randomBytes: (size) => {
    assert.equal(size, 32);
    return Buffer.from(fixed);
  } });
  const issued = store.issue(baseClaims());
  assert.equal(issued.ticket, fixed.toString("base64url"));
});
